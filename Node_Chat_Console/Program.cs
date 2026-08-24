using Node.Chat.Core.Crypto;
using System.Net;
using System.Net.Sockets;
using System.Text;

var crypto = new CryptoService();
const int PORT = 11000;

Console.WriteLine("Выбери роль:");
Console.WriteLine("1. Получатель ");
Console.WriteLine("2. Отправитель ");
Console.Write("Ввод: ");
var role = Console.ReadLine();

if (role == "1")
{
    // --- ЛОГИКА ПОЛУЧАТЕЛЯ ---
    Console.WriteLine("\n[Получатель] Генерация ключей...");
    var (myPub, myPriv) = crypto.GenerateKeyPair();
    Console.WriteLine($"[Получатель] Твой публичный ключ:\n{Convert.ToBase64String(myPub)}\n");

    Console.Write("[Получатель] Вставь публичный ключ Отправителя и нажми Enter: ");
    var senderPubStr = Console.ReadLine();
    var senderPub = Convert.FromBase64String(senderPubStr);

    var sharedSecret = crypto.ComputeSharedSecret(myPriv, senderPub);
    Console.WriteLine("[Получатель] Общий секрет вычислен. Ожидаю сообщений...\n");

    // Запуск UDP сервера
    using var listener = new UdpClient(PORT);
    var groupEP = new IPEndPoint(IPAddress.Any, PORT);

    Console.WriteLine($"[Получатель] Слушаю порт {PORT}...");
    while (true)
    {
        try
        {
            byte[] bytes = listener.Receive(ref groupEP);
            Console.WriteLine($"[Получатель] Получено {bytes.Length} байт от {groupEP.Address}");
            
            var decrypted = crypto.Decrypt(bytes, sharedSecret);
            var message = Encoding.UTF8.GetString(decrypted);
            Console.WriteLine($"[Получатель] Расшифровано: {message}\n");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Ошибка] {e.Message}");
        }
    }
}
else if (role == "2")
{
    // --- ЛОГИКА ОТПРАВИТЕЛЯ ---
    Console.WriteLine("\n[Отправитель] Генерация ключей...");
    var (myPub, myPriv) = crypto.GenerateKeyPair();
    Console.WriteLine($"[Отправитель] Твой публичный ключ:\n{Convert.ToBase64String(myPub)}\n");

    Console.Write("[Отправитель] Вставь публичный ключ Получателя и нажми Enter: ");
    var receiverPubStr = Console.ReadLine();
    var receiverPub = Convert.FromBase64String(receiverPubStr);

    var sharedSecret = crypto.ComputeSharedSecret(myPriv, receiverPub);
    
    Console.Write("[Отправитель] Введи IP-адрес Получателя (например, 192.168.1.1): ");
    var targetIp = Console.ReadLine();

    using var sender = new UdpClient();
    var endPoint = new IPEndPoint(IPAddress.Parse(targetIp), PORT);

    Console.WriteLine("[Отправитель] Готов! Пиши сообщения и жми Enter (для выхода напиши 'exit'):\n");
    while (true)
    {
        var text = Console.ReadLine();
        if (text.ToLower() == "exit") break;

        if (!string.IsNullOrEmpty(text))
        {
            var messageBytes = Encoding.UTF8.GetBytes(text);
            var encrypted = crypto.Encrypt(messageBytes, sharedSecret);
            
            sender.Send(encrypted, encrypted.Length, endPoint);
            Console.WriteLine($"[Отправитель] Отправлено {encrypted.Length} байт.\n");
        }
    }
}
else
{
    Console.WriteLine("Неверный выбор.");
};