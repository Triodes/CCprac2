using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetwProg
{
    class Client
    {
        public int RemotePort { get; private set; }
        TcpClient socket;
        StreamReader clientIn;
        StreamWriter clientOut;

        bool keepAlive = true;

        public delegate void MessageRecievedHandler(string message, int remotePort);

        public event MessageRecievedHandler MessageRecieved;

        public delegate void ConnectionCLosedHandler(int remotePort);

        public event ConnectionCLosedHandler ConnectionClosed;

        public Client(TcpClient socket)
        {
            this.socket = socket;
            clientIn = new StreamReader(socket.GetStream());
            clientOut = new StreamWriter(socket.GetStream());

            clientOut.AutoFlush = true;

            clientOut.WriteLine(Program.myPort);

            RemotePort = int.Parse(clientIn.ReadLine());

            Console.WriteLine("Verbonden: " + Program.ConvertToPort(RemotePort));

            Thread t = new Thread(ReadMessages);
            t.Start();
        }

        public void SendMessage(string message)
        {
            clientOut.WriteLine(message);
        }

        public enum DisconnectReason
        {
            Command,
            Message,
        }

        public void Disconnect(DisconnectReason reason)
        {
            keepAlive = false;
            if (reason == DisconnectReason.Command) SendMessage("closing");
            socket.Close();
            clientOut.Dispose();
            clientIn.Dispose();
            if (ConnectionClosed != null)
                ConnectionClosed(RemotePort);
            Console.WriteLine("Verbroken: " + RemotePort);
            Console.WriteLine("//Disconnected from: " + RemotePort + " | Reason: " + reason);
        }

        void ReadMessages()
        {
            while (keepAlive)
            {
                try
                {
                    string s = clientIn.ReadLine();
                    if (s == "closing")
                        Disconnect(DisconnectReason.Message);
                    else
                        MessageRecieved(s, RemotePort);
                }
                catch { }
            }
        }

    }
}
