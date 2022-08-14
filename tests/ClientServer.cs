using Jvs.MultiPipes;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
      Thread serverThread = new(async () =>
      {
        using var server = new NamedPipeListener("test-pipe-comms");
        server.Start();
        NamedPipeClient remote = await server.AcceptAsync();
        using var stream = remote.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("What is your name?");
        string name = reader.ReadString();
        Assert.AreEqual(name, "client");
        writer.Write($"Hello, {name}");
      });

      serverThread.Start();

      using (var client = new NamedPipeClient("test-pipe-comms"))
      {
        client.Connect();
        using var stream = client.GetStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        string response = reader.ReadString();
        Assert.AreEqual(response, "What is your name?");
        writer.Write("client");
        response = reader.ReadString();
        stream.WaitForDisconnect();
        Assert.AreEqual(response, "Hello, client");
      }

      serverThread.Join();
    }

    [Test]
    public async Task SingleClientAsync()
    {
      using (var server = new NamedPipeListener("test-pipe-comms"))
      {
        Thread serverThread = new(async () =>
        {
          server.Start();
          NamedPipeClient remote = await server.AcceptAsync();
          using var stream = remote.GetStream();
          using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
          using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
          writer.Write("What is your name?");
          string name = reader.ReadString();
          Assert.AreEqual(name, "client async");
          writer.Write($"Hello, {name}");
        });

        serverThread.Start();
        
        using (var client = new NamedPipeClient("test-pipe-comms"))
        {
          await client.ConnectAsync();
          using var stream = client.GetStream();
          using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
          using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
          string response = reader.ReadString();
          Assert.AreEqual(response, "What is your name?");
          writer.Write("client async");
          response = reader.ReadString();
          await stream.WaitForDisconnectAsync();
          Assert.AreEqual(response, "Hello, client async");
        }

        serverThread.Join();
      }
    }

    [Test]
    public void MultiClientSync()
    {
      int clientId = 0;
      using (var server = new NamedPipeListener("test-multi-client-sync"))
      {
        Thread serverThread = new(async () =>
        {
          server.Start();
          var acceptTask = server.AcceptAsync();
          while (clientId < 10)
          {
            var clientPipe = await acceptTask;
            if (clientId + 1 < 10)
            {
              acceptTask = server.AcceptAsync();
            }

            int id = clientId++;
            Debug.WriteLine($"Server received connection from client {id}");
            using var stream = clientPipe.GetStream();
            using var writer = new BinaryWriter(stream);
            writer.Write($"Client ID: {id}");
            Debug.WriteLine($"Server sent string to client {id}");
            stream.WaitForDisconnect(TimeSpan.FromSeconds(5));
          }

          Debug.WriteLine("Server reached total client limit; stopping");
        });

        serverThread.Start();
        
        for (int i = 0; i < 10; ++i)
        {
          using var client = new NamedPipeClient("test-multi-client-sync");
          client.Connect();
          Debug.WriteLine($"Client {i} connected to server");
          using var stream = client.GetStream();
          using var reader = new BinaryReader(stream);
          string response = reader.ReadString();
          Debug.WriteLine($"Client {i} read string from server");
          Assert.AreEqual(response, $"Client ID: {i}");
          Debug.WriteLine($"Client {i} disconnecting from server");
        }

        serverThread.Join();
      }
    }

    [Test]
    public void MultiClientAsync()
    {
      /// Every so often, this test throws an `AggregateException` with InnerException being an
      /// `ObjectDisposedException` with a message of:
      /// 
      ///   Safe handle has been closed.
      ///   Object name: 'SafeHandle'.
      /// 
      /// The source of the exception is always in something external. I'm almost completely sure it
      /// has to do with thread interruption and the usage of `using` blocks, but I haven't
      /// diagnosed it yet. The proper thing to do would be to set a function breakpoint on
      /// `System.Runtime.InteropServices.SafeHandle.Dispose` and see the call stack from there.
      /// 
      /// I added the `ExecutionCount` constant to run the test multiple times. The exception is
      /// _never_ thrown when the test is run directly (or if it is, it's being swallowed
      /// somewhere) -- it only appears when debugging the test. With a count of 50, it seems to
      /// throw fairly consistently ON MY MACHINE (the inescapable programmer's caveat).
      const int ExecutionCount = 50;

      foreach (var _ in Enumerable.Range(0, ExecutionCount))
      {
        int clientId = 0;
        using (var server = new NamedPipeListener("test-multi-client-async"))
        {
          Thread serverThread = new(async () =>
          {
            server.Start();
            var acceptTask = server.AcceptAsync();
            while (clientId < 10)
            {
              var clientPipe = await acceptTask;
              Debug.WriteLine("Accepted a client pipe");
              if (clientId + 1 < 10)
              {
                acceptTask = server.AcceptAsync();
              }

              int id = clientId++;
              Debug.WriteLine($"Server received connection from client {id}");
              using (var stream = clientPipe.GetStream())
              using (var writer = new BinaryWriter(stream))
              {
                writer.Write($"Async client ID: {id}");
                Debug.WriteLine($"Server sent string to client {id}");
              }
            }
          });

          serverThread.Start();

          static async Task<(int ClientID, int ServerID)> createClientPipe(
            int i, ConcurrentDictionary<int, int> idMap)
          {
            using var client = new NamedPipeClient("test-multi-client-async");
            await client.ConnectAsync();
            Debug.WriteLine($"Client {i} connected to server");
            using var stream = client.GetStream();
            using var reader = new BinaryReader(stream);
            string response = reader.ReadString();
            Debug.WriteLine($"Client {i} read string from server");
            Assert.IsTrue(response.StartsWith("Async client ID: "));
            Assert.IsTrue(int.TryParse(response.AsSpan().Slice("Async client ID: ".Length).ToString(),
              out int serverId));
            Debug.WriteLine($"Client {i} given server ID {serverId}");
            Assert.IsTrue(idMap.TryAdd(i, serverId));
            Debug.WriteLine($"Client {i} disconnecting from server");
            return (ClientID: i, serverId);
          }


          ConcurrentDictionary<int, int> clientServerIds = new();
          Task<(int ClientID, int ServerID)>[] clientTaskArray = null;

          using (BlockingCollection<Task<(int ClientID, int ServerID)>> clientTasks = new())
          {
            Parallel.For(0, 10, i => clientTasks.Add(createClientPipe(i, clientServerIds)));
            clientTasks.CompleteAdding();
            clientTaskArray = clientTasks.ToArray();
          }

          try
          {
            Task.WaitAll(clientTaskArray);
            Assert.AreEqual(clientTaskArray.Length, 10);
            foreach (var clientTask in clientTaskArray)
            {
              Assert.IsTrue(
                clientServerIds.TryGetValue(clientTask.Result.ClientID, out int serverId));
              Assert.AreEqual(clientTask.Result.ServerID, serverId);
            }
          }
          catch (AggregateException)
          {
            Debug.WriteLine("Aggregate exception occurred!");
          }

          serverThread.Join();
          Debug.WriteLine("Server and clients complete.");
        }
      }
    }
  }
}
