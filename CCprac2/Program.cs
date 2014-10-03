using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace NetwProg
{
    class Program
    {
        public static int myPort;
        int[] myNeighbors;
        public Dictionary<int, Client> connections;
        static void Main(string[] args)
        {
            Program p = new Program(args);
        }

        public Program(string[] input)
        {
            myPort = int.Parse(input[0]);
            myNeighbors = new int[input.Length - 1];
            for (int i = 1; i < input.Length; i++)
            {
                myNeighbors[i-1] = int.Parse(input[i]);
            }

            connections = new Dictionary<int, Client>();

            Console.Title = "Netchange " + myPort;          

            Listener listener = new Listener();
            Thread t = new Thread(listener.Listen);
            t.Start(this);

            for (int i = 0; i < myNeighbors.Length; i++)
            {
                if (myNeighbors[i] > myPort)
                {
                    TcpClient c = new TcpClient("localhost", myNeighbors[i]);
                    AddClient(c);
                }
            }

            while (true)
            {
                string[] s = Console.ReadLine().Split();
                if (s[0] == "B")
                {
                    int remotePort = int.Parse(s[1]);
                    if (connections.ContainsKey(remotePort))
                        connections[remotePort].SendMessage(s[2]);
                    else
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", remotePort));
                }
                else if (s[0] == "D")
                {
                    int remotePort = int.Parse(s[1]);
                    if (connections.ContainsKey(remotePort))
                        connections[remotePort].Disconnect(Client.DisconnectReason.Command);
                    else
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", remotePort));
                }
                else if (s[0] == "C")
                {
                    int remotePort = int.Parse(s[1]);
                    TcpClient c = new TcpClient("localhost", remotePort);
                    AddClient(c);
                }
            }
            

            t.Join();
        }

        public void AddClient(TcpClient temp)
        {
            Client client = new Client(temp);
            client.MessageRecieved += HandleMessage;
            client.ConnectionClosed += ConnectionClosed;
            connections.Add(client.RemotePort, client);
        }

        public void HandleMessage(string message, int remotePort)
        {
            Console.WriteLine("//Message recieved from: " + remotePort);
            Console.WriteLine(message);
        }

        public void ConnectionClosed(int remotePort)
        {
            connections.Remove(remotePort);
        }
    }

    class Listener
    {
        TcpListener server;
        public void Listen(object p)
        {
            server = new TcpListener(IPAddress.Any, Program.myPort);
            server.Start();
            Console.WriteLine("//listening on port " + Program.myPort);
            try
            {
                while (true)
                {
                    TcpClient temp = server.AcceptTcpClient();               
                    Program pr = (p as Program);
                    pr.AddClient(temp);
                }
            }
            catch { }

        }

        public void Stop()
        {
            server.Stop();
        }
    }

}
