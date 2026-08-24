using Node.Chat.Core.Crypto;
using System.Net;
using System.Net.Sockets;
using System.Text;

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