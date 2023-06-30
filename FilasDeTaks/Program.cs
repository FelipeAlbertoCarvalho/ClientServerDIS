using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    private TcpListener listener;
    private bool isRunning;
    private Queue<(TcpClient, string)> pendingRequestsHighMemory;
    private Queue<(TcpClient, string)> pendingRequestsLowMemory;

    public Server()
    {
        listener = new TcpListener(IPAddress.Any, 8080);
        pendingRequestsHighMemory = new Queue<(TcpClient, string)>();
        pendingRequestsLowMemory = new Queue<(TcpClient, string)>();
    }

    public async Task Start()
    {
        listener.Start();
        isRunning = true;

        Console.WriteLine("Servidor iniciado. Aguardando conexões...");

        while (isRunning)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Console.WriteLine("Cliente conectado!");

            if (MemoryUsageBelowThreshold())
            {
                pendingRequestsLowMemory.Enqueue((client, $"Tarefa #{pendingRequestsLowMemory.Count + 1}"));
                Console.WriteLine("Requisição adicionada à fila de baixa memória para execução posterior");
            }
            else
            {
                pendingRequestsHighMemory.Enqueue((client, $"Tarefa #{pendingRequestsHighMemory.Count + 1}"));
                Console.WriteLine("Requisição adicionada à fila de alta memória para execução posterior");
            }
        }
    }

    public void Stop()
    {
        isRunning = false;
        listener.Stop();
    }

    public async Task ProcessClientAsync(TcpClient client, string taskName)
    {
        try
        {
            NetworkStream stream = client.GetStream();

            byte[] buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string requestData = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            Console.WriteLine($"[{taskName}] Dados recebidos do cliente: {requestData}");

            await Task.Delay(30000);

            string responseData = "Resposta do servidor";
            byte[] responseBuffer = Encoding.UTF8.GetBytes(responseData);
            await stream.WriteAsync(responseBuffer, 0, responseBuffer.Length);

            Console.WriteLine($"[{taskName}] Resposta enviada ao cliente");

            client.Close();

            if (pendingRequestsHighMemory.Count > 0 && MemoryUsageBelowThreshold())
            {
                var (pendingClient, pendingTaskName) = pendingRequestsHighMemory.Dequeue();
                Console.WriteLine($"[{taskName}] Processando requisição pendente de alta memória - {pendingTaskName}...");

                _ = ProcessClientAsync(pendingClient, pendingTaskName);
            }
            else if (pendingRequestsLowMemory.Count > 0)
            {
                var (pendingClient, pendingTaskName) = pendingRequestsLowMemory.Dequeue();
                Console.WriteLine($"[{taskName}] Processando requisição pendente de baixa memória - {pendingTaskName}...");

                _ = ProcessClientAsync(pendingClient, pendingTaskName);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{taskName}] Erro ao processar cliente: {ex.Message}");
        }
    }

    private bool MemoryUsageBelowThreshold()
    {
        PerformanceCounter performanceCounter = new PerformanceCounter("Memory", "Available Bytes");
        float availableMemoryBytes = performanceCounter.NextValue();

        float totalMemoryBytes = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
        float usedMemoryPercent = (totalMemoryBytes - availableMemoryBytes) / totalMemoryBytes * 100;

        return usedMemoryPercent < 75;
    }
}

public class Program
{
    public static async Task Main()
    {
        Server server = new Server();
        Task serverTask = server.Start();

        // Simular chegada de 20 requisições a cada 15 segundos
        for (int i = 1; i <= 20; i++)
        {
            await Task.Delay(15000);
            TcpClient client = new TcpClient("localhost", 8080);
            Console.WriteLine("Cliente conectado!");
            serverTask = serverTask.ContinueWith(_ => server.ProcessClientAsync(client, $"Tarefa #{i}"));
        }

        await serverTask;
    }
}
