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
        const int maxNodes = 20;
        const int portLowerBound = 55500;

        public static int myPort;
        List<int> myNeighbors;
        public Client[] connections;
        static void Main(string[] args)
        {
            Program p = new Program(args);
        }

        public Program(string[] input)
        {
            myPort = ConvertFromPort(int.Parse(input[0]));
            myNeighbors = new List<int>();
            for (int i = 1; i < input.Length; i++)
            {
                myNeighbors.Add(ConvertFromPort(int.Parse(input[i])));
            }

            connections = new Client[maxNodes];

            Console.Title = "Netchange " + ConvertToPort(myPort);          

            Listener listener = new Listener();
            Thread t = new Thread(listener.Listen);
            t.Start(this);

            Init();

            for (int i = 0; i < myNeighbors.Count; i++)
            {
                if (myNeighbors[i] > myPort)
                {
                    TcpClient c = new TcpClient("localhost", myNeighbors[i]);
                    AddClient(c);
                }
            }

            Thread.Sleep(1500 + 3000 * myPort);

            SendInit();

            Init();

            ReadConsoleInput();
        }

        #region Input Handling

        private void ReadConsoleInput()
        {
            while (true)
            {
                string[] s = Console.ReadLine().Split();
                if (s[0] == "B")
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));
                    string message = string.Join(" ",s.Skip(2));
                    if (remotePort == myPort)
                    {
                        Console.WriteLine(message);
                    }                    
                    else if (Nb[remotePort] != -1)
                    {                      
                        SendTextMessage(Nb[remotePort], remotePort, message);
                    }
                    else
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", ConvertToPort(remotePort)));
                }
                else if (s[0] == "D")
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));
                    if (Nb[remotePort] != -1)
                        connections[remotePort].Disconnect(Client.DisconnectReason.Command);
                    else
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", ConvertToPort(remotePort)));
                }
                else if (s[0] == "C")
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));
                    TcpClient c = new TcpClient("localhost", remotePort);
                    AddClient(c);
                    connections[remotePort].SendMessage("new," + myPort);
                    NewConnection(remotePort);
                }
                else if (s[0] == "R")
                {
                    PrintTable();
                }
            }
        }

        private void PrintTable()
        {
            for (int i = 0; i < maxNodes; i++)
            {
                string prefered = i == myPort ? "local" : ConvertToPort(Nb[i]).ToString();
                int distance = D[i];
                //if (distance < maxNodes)
                    Console.WriteLine("{0} {1} {2}", ConvertToPort(i), distance, prefered);
            }
        }

        #endregion

        #region Client Handling

        private void HandleMessage(string message, int remotePort)
        {
            string[] mParts = message.Split(',');
            if (mParts[0] == "myDist")
            {
                int v = int.Parse(mParts[1]);
                ndis[remotePort][v] = int.Parse(mParts[2]);
                Console.WriteLine("//got mydist from {0}: {1},{2}", remotePort, mParts[1], mParts[2]);
                Recompute(v);
            }
            else if (mParts[0] == "message")
            {
                int destination = int.Parse(mParts[1]);
                if (destination == myPort)
                {
                    Console.WriteLine(mParts[2]);
                }
                else
                {
                    int nextHop = Nb[destination];
                    SendTextMessage(nextHop, destination, mParts[2]);
                    Console.WriteLine("//Message for: {0} forwarded to: {1}", destination, nextHop);
                }
            }
            else if (mParts[0] == "new")
            {
                NewConnection(int.Parse(mParts[1]));
            }
            else
            {
                Console.WriteLine("//unknown message type");
            }
        }

        public void AddClient(TcpClient temp)
        {
            Client client = new Client(temp);
            client.MessageRecieved += HandleMessage;
            client.ConnectionClosed += ConnectionClosed;
            connections[client.RemotePort] = client;
        }

        private void NewConnection(int remotePort)
        {
            myNeighbors.Add(remotePort);
            for (int i = 0; i < maxNodes; i++)
            {
                ndis[remotePort][i] = maxNodes;
                SendMyDist(remotePort, i, D[i]);
            }
        }

        private void ConnectionClosed(int remotePort)
        {
            connections[remotePort] = null;
            myNeighbors.Remove(remotePort);
            for (int v = 0; v < maxNodes; v++)
            {
                if (Nb[v] == remotePort)
                    Recompute(v);
            }
        }

        private void SendMyDist(int remotePort, int node, int value)
        {
            connections[remotePort].SendMessage(String.Format("myDist,{0},{1}", node, value));
            Console.WriteLine("//sent mydist to {0}: {1},{2}", remotePort, node, value);
        }

        private void SendTextMessage(int prefered, int destination, string message)
        {
            connections[prefered].SendMessage(String.Format("message,{0},{1}", destination, message));
        }

        #endregion

        #region Port Conversion

        public static int ConvertFromPort(int port)
        {
            return port - portLowerBound;
        }

        public static int ConvertToPort(int index)
        {
            return index + portLowerBound;
        }

        #endregion

        #region Routing

        int[][] ndis = new int[maxNodes][];
        int[] D = new int[maxNodes];
        int[] Nb = new int[maxNodes];

        private void Init()
        {
            for (int i = 0; i < maxNodes; i++)
            {
                ndis[i] = new int[maxNodes];
                for (int j = 0; j < maxNodes; j++)
                {
                    ndis[i][j] = maxNodes;
                }
            }

            for (int i = 0; i < maxNodes; i++)
            {
                D[i] = maxNodes;
                Nb[i] = -1;
            }

            D[myPort] = 0;
            Nb[myPort] = myPort;
        }

        private void SendInit()
        {
            Parallel.For(0, myNeighbors.Count, (int iterator) =>
            {
                SendMyDist(myNeighbors[iterator], myPort, 0);
            });
        }

        private void Recompute(int remotePort)
        {
            int CurrentDv = D[remotePort], bestNb = -1;
            if (remotePort == myPort)
            {
                D[remotePort] = 0;
                Nb[remotePort] = remotePort;
            }
            else
            {
                int d = int.MaxValue;
                for (int i = 0; i < myNeighbors.Count; i++)
                {
                    int temp = ndis[myNeighbors[i]][remotePort];
                    if (temp < d)
                    {
                        d = temp;
                        bestNb = myNeighbors[i];
                    }
                }

                d++;

                if (d < maxNodes)
                {
                    D[remotePort] = d;
                    Nb[remotePort] = bestNb;
                }
                else
                {
                    D[remotePort] = maxNodes;
                    Nb[remotePort] = -1;
                }
            }

            if (D[remotePort] != CurrentDv)
            {
                Console.WriteLine("Afstand naar {0} is nu {1} via {2}", ConvertToPort(remotePort), D[remotePort], ConvertToPort(bestNb));
                Parallel.For(0, myNeighbors.Count, (int iterator) =>
                {
                    SendMyDist(myNeighbors[iterator], remotePort, D[remotePort]);
                });
            }
        }

        #endregion
    }
}
