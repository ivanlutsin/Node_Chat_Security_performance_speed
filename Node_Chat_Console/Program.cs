using System.Net;
using System.Net.Sockets;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Node.Chat.Core.Crypto;
using System.IO.Ports;
using InTheHand.Bluetooth;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

// Фиксация режима 1 - WIFI Direct (UDP) 2 - BLE (Bluetooth Low Energy)
// пусто - режим выбирается после запуска
const string DEBUG_MODE = ""; 

Console.WriteLine("Добро пожаловать в Node Chat");
Task.Delay(500).Wait();
Console.WriteLine("Компиляция режимов связи");
Task.Delay(1000).Wait();
Console.WriteLine("Выбери режим работы:");
Console.WriteLine("1. UDP (Wi-Fi / Локальная сеть)");
Console.WriteLine("2. BLUETOOTH");
Console.WriteLine("Введите номер режима");
string selectedMode = Console.ReadLine();

if (selectedMode == "1")
{
    RunUdpMode();
}
else if (selectedMode == "2")
{
    await RunBluetoothEncrypted();
}

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
// РЕЖИМ 2: BLUETOOTH
// ==========================================
async Task RunBluetoothEncrypted()
{
    Console.WriteLine("\n=== ЗАПУСК CLASSIC BLUETOOTH ===");
    Console.WriteLine("Выбери роль:");
    Console.WriteLine("1. Сервер (ждет подключения)");
    Console.WriteLine("2. Клиент (подключается)");
    Console.Write("Ввод: ");
    var role = Console.ReadLine();

    if (role == "1")
    {
        // ============ СЕРВЕР ============
        Console.WriteLine("\n[Система] Создание RFCOMM сервера...");
        
        try
        {
            var listener = new BluetoothListener(BluetoothService.SerialPort);
            listener.Start();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("✅ СЕРВЕР ЗАПУЩЕН");
            Console.ResetColor();
            Console.WriteLine("Жду подключения клиента...\n");
            
            var client = await listener.AcceptBluetoothClientAsync();
            var stream = client.GetStream();
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ CONNECTED!");
            Console.ResetColor();

            // === ГЕНЕРАЦИЯ КЛЮЧЕЙ ===
            Console.WriteLine("\n[Криптография] Генерация пары ключей X25519...");
            var crypto = new CryptoService();
            var (myPub, myPriv) = crypto.GenerateKeyPair();
            
            Console.WriteLine($"\n[Твой публичный ключ]:\n{Convert.ToBase64String(myPub)}\n");
            
            // Отправляем свой публичный ключ
            var myPubKeyBytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(myPub));
            await stream.WriteAsync(myPubKeyBytes, 0, myPubKeyBytes.Length);
            await stream.FlushAsync();
            
            // Получаем публичный ключ собеседника
            var buffer = new byte[2048];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var theirPubStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var theirPub = Convert.FromBase64String(theirPubStr);
            
            Console.WriteLine($"\n[Публичный ключ собеседника]:\n{theirPubStr}\n");
            
            // Вычисляем общий секрет
            var sharedSecret = crypto.ComputeSharedSecret(myPriv, theirPub);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ ОБЩИЙ СЕКРЕТ ВЫЧИСЛЕН!");
            Console.WriteLine("🔐 Все сообщения шифруются (XChaCha20-Poly1305)");
            Console.ResetColor();
            Console.WriteLine("\nМожно писать сообщения (для выхода 'exit'):\n");
            Console.Write("> ");

            // Чтение в фоновом потоке (с расшифровкой)
            Task.Run(async () =>
            {
                var readBuffer = new byte[4096];
                while (true)
                {
                    try
                    {
                        var readBytes = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                        if (readBytes > 0)
                        {
                            var encrypted = new byte[readBytes];
                            Array.Copy(readBuffer, encrypted, readBytes);
                            
                            var decrypted = crypto.Decrypt(encrypted, sharedSecret);
                            var message = Encoding.UTF8.GetString(decrypted);
                            
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Собеседник]: {message}");
                            Console.ResetColor();
                            Console.Write("> ");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n[Ошибка чтения]: {ex.Message}");
                        break;
                    }
                }
            });

            // Отправка (с шифрованием)
            while (true)
            {
                var text = Console.ReadLine();
                if (text.ToLower() == "exit") break;

                if (!string.IsNullOrEmpty(text))
                {
                    var messageBytes = Encoding.UTF8.GetBytes(text);
                    var encrypted = crypto.Encrypt(messageBytes, sharedSecret);
                    
                    await stream.WriteAsync(encrypted, 0, encrypted.Length);
                    await stream.FlushAsync();
                    
                    Console.WriteLine($"[Отправлено {encrypted.Length} байт (зашифровано)]");
                    Console.Write("> ");
                }
            }

            client.Close();
            listener.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ОШИБКА: {ex.Message}");
        }
    }
    else if (role == "2")
    {
        // ============ КЛИЕНТ ============
        Console.WriteLine("\n[Система] Поиск устройств...");
        
        try
        {
            var client = new BluetoothClient();
            var devices = client.DiscoverDevices();
            var deviceList = devices.ToList();
            
            Console.WriteLine($"Найдено устройств: {deviceList.Count}\n");
            
            for (int i = 0; i < deviceList.Count; i++)
            {
                var device = deviceList[i];
                if (device.DeviceName != null && device.DeviceName.Length > 0)
                {
                    Console.WriteLine($"{i + 1}. {device.DeviceName} ({device.DeviceAddress})");
                }
            }
            
            Console.Write("\nВыбери устройство (номер): ");
            var choice = Console.ReadLine();
            
            if (int.TryParse(choice, out int index) && index > 0 && index <= deviceList.Count)
            {
                var targetDevice = deviceList[index - 1];
                
                Console.WriteLine($"\n[Система] Подключение к {targetDevice.DeviceName}...");
                
                await client.ConnectAsync(targetDevice.DeviceAddress, BluetoothService.SerialPort);
                
                var stream = client.GetStream();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ CONNECTED!");
                Console.ResetColor();

                // === ГЕНЕРАЦИЯ КЛЮЧЕЙ ===
                Console.WriteLine("\n[Криптография] Генерация пары ключей X25519...");
                var crypto = new CryptoService();
                var (myPub, myPriv) = crypto.GenerateKeyPair();
                
                Console.WriteLine($"\n[Твой публичный ключ]:\n{Convert.ToBase64String(myPub)}\n");
                
                // Получаем публичный ключ собеседника (он отправил первым)
                var buffer = new byte[2048];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                var theirPubStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var theirPub = Convert.FromBase64String(theirPubStr);
                
                Console.WriteLine($"\n[Публичный ключ собеседника]:\n{theirPubStr}\n");
                
                // Отправляем свой публичный ключ
                var myPubKeyBytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(myPub));
                await stream.WriteAsync(myPubKeyBytes, 0, myPubKeyBytes.Length);
                await stream.FlushAsync();
                
                // Вычисляем общий секрет
                var sharedSecret = crypto.ComputeSharedSecret(myPriv, theirPub);
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ ОБЩИЙ СЕКРЕТ ВЫЧИСЛЕН!");
                Console.WriteLine("🔐 Все сообщения шифруются (XChaCha20-Poly1305)");
                Console.ResetColor();
                Console.WriteLine("\nМожно писать сообщения (для выхода 'exit'):\n");
                Console.Write("> ");

                // Чтение в фоновом потоке (с расшифровкой)
                Task.Run(async () =>
                {
                    var readBuffer = new byte[4096];
                    while (true)
                    {
                        try
                        {
                            var readBytes = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                            if (readBytes > 0)
                            {
                                var encrypted = new byte[readBytes];
                                Array.Copy(readBuffer, encrypted, readBytes);
                                
                                var decrypted = crypto.Decrypt(encrypted, sharedSecret);
                                var message = Encoding.UTF8.GetString(decrypted);
                                
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[Собеседник]: {message}");
                                Console.ResetColor();
                                Console.Write("> ");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\n[Ошибка чтения]: {ex.Message}");
                            break;
                        }
                    }
                });

                // Отправка (с шифрованием)
                while (true)
                {
                    var text = Console.ReadLine();
                    if (text.ToLower() == "exit") break;

                    if (!string.IsNullOrEmpty(text))
                    {
                        var messageBytes = Encoding.UTF8.GetBytes(text);
                        var encrypted = crypto.Encrypt(messageBytes, sharedSecret);
                        
                        await stream.WriteAsync(encrypted, 0, encrypted.Length);
                        await stream.FlushAsync();
                        
                        Console.WriteLine($"[Отправлено {encrypted.Length} байт (зашифровано)]");
                        Console.Write("> ");
                    }
                }

                client.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ОШИБКА: {ex.Message}");
            Console.WriteLine($"Детали: {ex.InnerException?.Message}");
        }
    }
}