using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetwProg
{
    class Program
    {
        const int maxNodes = 20;
        const int portLowerBound = 55500;

        public static int myPort;
        List<int> myNeighbors;
        public Client[] connections;
        public Queue<string>[] messageQueues;
        AutoResetEvent[] waiter;
        int cycleLimit;

        object locker, cycleLock;
        static void Main(string[] args)
        {
            Program p = new Program(args);
        }

        public Program(string[] input)
        {
            //create locking object
            locker = new object();
            cycleLock = new object();

            //parse my port
            myPort = ConvertFromPort(int.Parse(input[0]));

            //parse and make list of neighboring ports
            myNeighbors = new List<int>();
            for (int i = 1; i < input.Length; i++)
            {
                myNeighbors.Add(ConvertFromPort(int.Parse(input[i])));
            }

            //create an array to accomodate for a connection to each port.
            connections = new Client[maxNodes];
            waiter = new AutoResetEvent[maxNodes];
            messageQueues = new Queue<string>[maxNodes];
            for (int i = 0; i < maxNodes; i++)
            {
                messageQueues[i] = new Queue<string>();
                waiter[i] = new AutoResetEvent(false);
            }

            cycleLimit = myNeighbors.Count + 1;
            //set the console title to port nr.
            Console.Title = "Netchange " + ConvertToPort(myPort);

            //set all routing information to its default values
            InitRoutingData();

            //listen for incoming connection requests
            Listener listener = new Listener();
            Thread t = new Thread(listener.Listen);
            t.Start(this);

            //Connect to neighboring hosts
            for (int i = 0; i < myNeighbors.Count; i++)
            {
                if (myNeighbors[i] > myPort)
                {
                    TcpClient c = new TcpClient("localhost", ConvertToPort(myNeighbors[i]));
                    AddClient(c);
                }
            }

            SendInit();

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
                    string message = string.Join(" ", s.Skip(2));
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
                    TcpClient c = new TcpClient("localhost", ConvertToPort(remotePort));
                    AddClient(c);
                    myNeighbors.Add(remotePort);
                    SendMessage("new," + myPort, remotePort);
                    waiter[remotePort].WaitOne();
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
                if (distance < maxNodes)
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
                int dist = int.Parse(mParts[2]);
                lock (cycleLock)
                {
                    if (v + 1 > cycleLimit && dist < maxNodes)
                    {
                        cycleLimit = v + 1;
                        Console.WriteLine("//found {0}, cycle limit is now {1}", v, cycleLimit);
                        for (int i = 0; i < maxNodes; i++)
                        {
                            if (rejectedOn[i] < cycleLimit)
                                Recompute(i);
                        }
                    }
                }
                //lock (DiscoveredNodes)
                //{

                //    if (!DiscoveredNodes[v] && dist != maxNodes)
                //    {
                //        DiscoveredNodes[v] = true;
                //        cycleLimit++;
                //        //Console.WriteLine("//found node {0}, nr of nodes is now {1}, value {2}", v, cycleLimit, dist);
                //    }
                //}
                ndis[remotePort][v] = dist;
                //Console.WriteLine("//got mydist from {0}: {1},{2}", remotePort, mParts[1], mParts[2]);
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
                    Console.WriteLine("Bericht voor {0} doorgestuurd naar {1}", ConvertToPort(destination), ConvertToPort(nextHop));
                }
            }
            else if (mParts[0] == "new")
            {
                int rp = int.Parse(mParts[1]);
                myNeighbors.Add(rp);
                SendMessage("OK," + myPort, rp);
                NewConnection(rp);
            }
            else if (mParts[0] == "OK")
            {
                waiter[int.Parse(mParts[1])].Set();
            }
            else
            {
                Console.WriteLine("//unknown message type");
            }
        }

        public void AddClient(TcpClient temp)
        {
            Client client = new Client(temp, HandleMessage, ConnectionClosed);
            connections[client.RemotePort] = client;
            client.SetQueue(messageQueues[client.RemotePort]);
        }

        private void NewConnection(int remotePort)
        {
            List<Tuple<int, int>> temp = new List<Tuple<int, int>>();
            for (int i = 0; i < maxNodes; i++)
            {
                temp.Add(new Tuple<int, int>(i, D[i]));
            }
            temp.Sort((a, b) =>
            {
                return a.Item2.CompareTo(b.Item2);
            });
            for (int i = 0; i < maxNodes; i++)
            {
                SendMyDist(remotePort, temp[i].Item1, temp[i].Item2);
                //Console.WriteLine("//sent md to: " + );
            }
        }

        private void ConnectionClosed(int remotePort)
        {
            connections[remotePort] = null;
            myNeighbors.Remove(remotePort);
            lock (messageQueues[remotePort])
            {
                messageQueues[remotePort].Clear();
            }
            for (int v = 0; v < maxNodes; v++)
            {
                ndis[remotePort][v] = maxNodes;
                if (Nb[v] == remotePort)
                    Recompute(v);
            }
        }

        private void SendMyDist(int remotePort, int node, int value)
        {
            SendMessage(String.Format("myDist,{0},{1}", node, value), remotePort);
            //Console.WriteLine("//sent mydist to {0}: {1},{2}", remotePort, node, value);
        }

        private void SendTextMessage(int prefered, int destination, string message)
        {
            SendMessage(String.Format("message,{0},{1}", destination, message), prefered);
        }

        private void SendMessage(string message, int remotePort)
        {
            lock (messageQueues[remotePort])
            {
                messageQueues[remotePort].Enqueue(message);
            }
            if (connections[remotePort] != null)
                connections[remotePort].SendMessage();
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

        int[] rejectedOn = new int[maxNodes];

        private void InitRoutingData()
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

                rejectedOn[i] = int.MaxValue;
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
            lock (locker)
            {
                int CurrentDv = D[remotePort], bestNb = -1, currNb = Nb[remotePort];
                if (remotePort == myPort)
                {
                    D[remotePort] = 0;
                    Nb[remotePort] = remotePort;
                }
                else
                {
                    int d = int.MaxValue - 1;
                    bool reachable;
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

                    lock (cycleLock)
                    {
                        if (d < cycleLimit)
                        {
                            D[remotePort] = d;
                            Nb[remotePort] = bestNb;
                            reachable = true;
                            rejectedOn[remotePort] = int.MaxValue;
                        }
                        else
                        {
                            Console.WriteLine("//port {0} unreachable on limit {1}", remotePort, cycleLimit);
                            D[remotePort] = maxNodes;
                            Nb[remotePort] = -1;
                            reachable = false;
                            rejectedOn[remotePort] = cycleLimit;
                        } 
                    }
                    if ((D[remotePort] != CurrentDv || currNb != Nb[remotePort]) && reachable)
                        Console.WriteLine("Afstand naar {0} is nu {1} via {2}", ConvertToPort(remotePort), D[remotePort], ConvertToPort(Nb[remotePort])); //bestNb or Nb[remotePort]???
                    if (!reachable)//D[remotePort] >= cycleLimit)
                        Console.WriteLine("Onbereikbaar: " + ConvertToPort(remotePort));
                }

                if (D[remotePort] != CurrentDv)
                {
                    Parallel.For(0, myNeighbors.Count, (int iterator) =>
                    {
                        SendMyDist(myNeighbors[iterator], remotePort, D[remotePort]);
                    });
                }
            }
        }

        #endregion
    }
}
