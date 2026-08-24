using Sodium;
using System;

namespace Node.Chat.Core.Crypto;

public class CryptoService
{
    public (byte[] PublicKey, byte[] PrivateKey) GenerateKeyPair()
    {
        var keyPair = PublicKeyBox.GenerateKeyPair();
        return (keyPair.PublicKey, keyPair.PrivateKey);
    }

    public byte[] ComputeSharedSecret(byte[] myPrivateKey, byte[] theirPublicKey)
    {
        return ScalarMult.Mult(myPrivateKey, theirPublicKey);
    }

    public byte[] Encrypt(byte[] message, byte[] key)
    {
        var nonce = SecretBox.GenerateNonce();
        var ciphertext = SecretBox.Create(message, nonce, key);
        
        // 24 байта - это жесткая длина nonce для XSalsa20-Poly1305
        var result = new byte[24 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 24);
        Buffer.BlockCopy(ciphertext, 0, result, 24, ciphertext.Length);
        
        return result;
    }

    public byte[] Decrypt(byte[] encryptedMessage, byte[] key)
    {
        var nonce = new byte[24];
        Buffer.BlockCopy(encryptedMessage, 0, nonce, 0, 24);
        
        var ciphertext = new byte[encryptedMessage.Length - 24];
        Buffer.BlockCopy(encryptedMessage, 24, ciphertext, 0, ciphertext.Length);
        
        return SecretBox.Open(ciphertext, nonce, key);
    }
}