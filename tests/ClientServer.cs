using NUnit.Framework;
using Jvs.MultiPipes;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Diagnostics;

namespace MultiPipesTest
{
  public class ClientServer
  {
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void SingleClient()
    {
      using (var server = new NamedPipeListener("test-pipe-comms"))
      {
        server.Start();
        server.AcceptAsync().ContinueWith(p =>
        {
          NamedPipeClient remote = p.Result;
          using (var stream = remote.GetStream())
          {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
              writer.Write("What is your name?");
              string name = reader.ReadString();
              Assert.AreEqual(name, "client");
              writer.Write($"Hello, {name}");
            }
          }
        });

        using (var client = new NamedPipeClient("test-pipe-comms"))
        {
          client.Connect();
          using (var stream = client.GetStream())
          using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
          using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
          {
            string response = reader.ReadString();
            Assert.AreEqual(response, "What is your name?");
            writer.Write("client");
            response = reader.ReadString();
            Assert.AreEqual(response, "Hello, client");
          }
        }
      }
    }

    [Test]
    public void MultiClientSync()
    {
      int clientId = 0;
      using (var server = new NamedPipeListener("test-multi-client-sync"))
      {
        Action<Task<NamedPipeClient>> acceptAction = null;
        acceptAction = p =>
        {
          int id = clientId++;
          Debug.WriteLine($"Server received connection from client {id}");
          NamedPipeClient pipe = p.Result;
          using (var stream = pipe.GetStream())
          using (var writer = new BinaryWriter(stream))
          {
            writer.Write($"Client ID: {id}");
            Debug.WriteLine($"Server sent string to client {id}");
            stream.WaitForDataOrDisconnect(TimeSpan.FromMinutes(5));
          }

          if (clientId < 10)
          {
            server.AcceptAsync().ContinueWith(acceptAction);
          }
        };

        server.Start();
        server.AcceptAsync().ContinueWith(acceptAction);

        for (int i = 0; i < 10; ++i)
        {
          using (var client = new NamedPipeClient("test-multi-client-sync"))
          {
            client.Connect();
            Debug.WriteLine($"Client {i} connected to server");
            using (var stream = client.GetStream())
            using (var reader = new BinaryReader(stream))
            {
              string response = reader.ReadString();
              Debug.WriteLine($"Client {i} read string from server");
              Assert.AreEqual(response, $"Client ID: {i}");
            }

            Debug.WriteLine($"Client {i} disconnected from server");
          }
        }
      }
    }
  }
}
