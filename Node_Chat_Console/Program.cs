using System.Net;
using System.Net.Sockets;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Node.Chat.Core.Crypto;

// Фиксация режима 1 - WIFI Direct (UDP) 2 - BLE (Bluetooth Low Energy)
// пусто - режим выбирается после запуска
const string DEBUG_MODE = ""; 

Console.WriteLine("Добро пожаловать в Node Chat");

string selectedMode = DEBUG_MODE;

// Если хак не задан, показываем меню
if (string.IsNullOrEmpty(selectedMode))
{
    Console.WriteLine("Компиляция режимов связи");
    Task.Delay(1000).Wait();
    Console.WriteLine("Выбери режим работы:");
    Console.WriteLine("1. UDP (Wi-Fi / Локальная сеть)");
    Console.WriteLine("2. BLE (Bluetooth Low Energy)");
    Console.Write("Ввод: ");
    var choice = Console.ReadLine();
    selectedMode = choice == "1" ? "UDP" : "BLE";
}
else
{
    Console.WriteLine($"[DEBUG] Автозапуск в режиме: {selectedMode} (см. константу DEBUG_MODE)\n");
}

if (selectedMode == "UDP")
{
    RunUdpMode();
}
else if (selectedMode == "BLE")
{
    RunBleMode();
}

// ==========================================
// РЕЖИМ 1: UDP (То, что работало с картошкой)
// ==========================================2
void RunUdpMode()
{
    Console.WriteLine("=== ЗАПУСК UDP РЕЖИМА ===");
    var crypto = new CryptoService();
    const int PORT = 11000;

    // Шаг 1: Генерация ключей
Console.WriteLine("[1/4] Генерация ключей...");
var (myPub, myPriv) = crypto.GenerateKeyPair();
Console.WriteLine($"Твой публичный ключ:\n{Convert.ToBase64String(myPub)}\n");

// Шаг 2: Обмен ключами
Console.Write("[2/4] Вставь публичный ключ собеседника: ");
var otherPubStr = Console.ReadLine();
var otherPub = Convert.FromBase64String(otherPubStr);

// Шаг 3: Вычисление общего секрета
Console.WriteLine("[3/4] Вычисление общего секрета...");
var sharedSecret = crypto.ComputeSharedSecret(myPriv, otherPub);
Console.WriteLine("✅ Общий секрет вычислен!\n");

// Шаг 4: Ввод IP собеседника
Console.Write("[4/4] Введи IP-адрес собеседника (например, 192.168.10.216): ");
var targetIp = Console.ReadLine();
var targetEndPoint = new IPEndPoint(IPAddress.Parse(targetIp), PORT);

Console.WriteLine("Канал создан");
Console.WriteLine("Пишите сообщение и жми Enter");

// Запускаем UDP-сокет
using var udpClient = new UdpClient(PORT);
var groupEP = new IPEndPoint(IPAddress.Any, PORT);

// Запускаем приемник в фоновом потоке
var cts = new CancellationTokenSource();
Task.Run(() =>
{
    Console.WriteLine("[Система] Приемник запущен. Ожидаю сообщений...\n");
    
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            byte[] bytes = udpClient.Receive(ref groupEP);
            var decrypted = crypto.Decrypt(bytes, sharedSecret);
            var message = Encoding.UTF8.GetString(decrypted);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Собеседник]: {message}");
            Console.ResetColor();
            Console.Write("> ");
        }
        catch (Exception e)
        {
            if (!cts.Token.IsCancellationRequested)
            {
                Console.WriteLine($"[Ошибка приема]: {e.Message}");
            }
        }
    }
}, cts.Token);

// Основной цикл отправки
Console.Write("> ");
while (true)
{
    var text = Console.ReadLine();
    
    if (text.ToLower() == "exit")
    {
        cts.Cancel();
        Console.WriteLine("\n[Система] Завершение работы...");
        break;
    }
    
    if (!string.IsNullOrEmpty(text))
    {
        var messageBytes = Encoding.UTF8.GetBytes(text);
        var encrypted = crypto.Encrypt(messageBytes, sharedSecret);
        
        udpClient.Send(encrypted, encrypted.Length, targetEndPoint);
        Console.Write("> ");
    }
}

// Ждем завершения фонового потока
Task.Delay(1000).Wait();
Console.WriteLine("До свидания!");
}

// ==========================================
// РЕЖИМ 2: BLE (Новый Dual-режим)
// ==========================================

async Task RunBleMode()
{
    Console.WriteLine("=== ЗАПУСК BLE РЕЖИМА ===");
    
    try
    {
        // 1. ВЕЩАТЕЛЬ
        Console.WriteLine("[Система] Настройка вещателя...");
        var publisher = new BluetoothLEAdvertisementPublisher();
        var manufacturerData = new BluetoothLEManufacturerData();
        manufacturerData.CompanyId = 0xFFFE;
        manufacturerData.Data = CryptographicBuffer.ConvertStringToBinary("NODE_CHAT", BinaryStringEncoding.Utf8);
        publisher.Advertisement.ManufacturerData.Add(manufacturerData);

        // 2. СКАНЕР
        Console.WriteLine("[Система] Настройка сканера...");
        var watcher = new BluetoothLEAdvertisementWatcher();
        BluetoothLEDevice connectedDevice = null;
        GattCharacteristic messageCharacteristic = null;

        watcher.Received += async (sender, args) =>
        {
            foreach (var data in args.Advertisement.ManufacturerData)
            {
                if (data.CompanyId == 0xFFFE)
                {
                    var rssi = args.RawSignalStrengthInDBm;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[НАЙДЕН NODE] Сигнал: {rssi} dBm");
                    Console.ResetColor();

                    // Подключаемся только если еще не подключены
                    if (connectedDevice == null && rssi > -80)
                    {
                        try
                        {
                            Console.WriteLine("[Система] Подключение...");
                            connectedDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                            
                            if (connectedDevice != null)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("[Система] ✅ Подключено! Можно писать сообщения.");
                                Console.ResetColor();
                                Console.Write("> ");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Ошибка подключения]: {ex.Message}");
                        }
                    }
                }
            }
        };

        // 3. ЗАПУСК
        publisher.Start();
        watcher.Start();

        Console.WriteLine("\n✅ BLE РЕЖИМ АКТИВИРОВАН");
        Console.WriteLine("Пиши сообщения и жми Enter (для выхода 'exit'):\n");
        Console.Write("> ");

        // 4. ЦИКЛ ОТПРАВКИ
        while (true)
        {
            var text = Console.ReadLine();
            if (text.ToLower() == "exit") break;

            if (!string.IsNullOrEmpty(text) && connectedDevice != null)
            {
                try
                {
                    // Получаем все сервисы устройства
                    var servicesResult = await connectedDevice.GetGattServicesAsync();
                    
                    foreach (var service in servicesResult.Services)
                    {
                        // Ищем наш сервис (по UUID)
                        var charsResult = await service.GetCharacteristicsAsync();
                        
                        foreach (var chr in charsResult.Characteristics)
                        {
                            // Пытаемся записать в любую характеристику
                            if ((chr.CharacteristicProperties & GattCharacteristicProperties.Write) != 0)
                            {
                                var buffer = CryptographicBuffer.ConvertStringToBinary(text, BinaryStringEncoding.Utf8);
                                await chr.WriteValueAsync(buffer);
                                Console.WriteLine($"[Отправлено]: {text}");
                                Console.Write("> ");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Ошибка отправки]: {ex.Message}");
                    Console.Write("> ");
                }
            }
            else if (connectedDevice == null)
            {
                Console.WriteLine("[Система] Еще не подключено. Жду собеседника...");
            }
        }

        // 5. ОСТАНОВКА
        watcher.Stop();
        publisher.Stop();
        Console.WriteLine("\n[Система] BLE выключен.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ ОШИБКА: {ex.Message}");
    }
}