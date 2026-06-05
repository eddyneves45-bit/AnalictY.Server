using System;
using BCrypt.Net.BCrypt;

class Program
{
    static void Main()
    {
        var password = Environment.GetEnvironmentVariable("HASH_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Defina HASH_PASSWORD antes de gerar um hash.");
        }

        string hash = BCrypt.HashPassword(password);
        Console.WriteLine(hash);
    }
}
