﻿using System;
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
        public Queue<string>[] messageQueues;
        AutoResetEvent[] waiter;

        bool[] DiscoveredNodes;
        int cycleLimit;

        object locker;
        static void Main(string[] args)
        {
            Program p = new Program(args);
        }

        public Program(string[] input)
        {
            //create locking object
            locker = new object();

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
            DiscoveredNodes = new bool[maxNodes];
            for (int i = 0; i < maxNodes; i++)
            {
                messageQueues[i] = new Queue<string>();
                waiter[i] = new AutoResetEvent(false);
                DiscoveredNodes[i] = false;
            }

            DiscoveredNodes[myPort] = true;
            for (int i = 0; i < myNeighbors.Count; i++)
            {
                DiscoveredNodes[myNeighbors[i]] = true;
            }
            cycleLimit = myNeighbors.Count + 1;
            //set the console title to port nr.
            Console.Title = "Netchange " + ConvertToPort(myPort);          

            //listen for incoming connection requests
            Listener listener = new Listener();
            Thread t = new Thread(listener.Listen);
            t.Start(this);

            //set all routing information to its default values
            InitRoutingData();

            //Connect to neighboring hosts
            for (int i = 0; i < myNeighbors.Count; i++)
            {
                if (myNeighbors[i] > myPort)
                {
                    TcpClient c = new TcpClient("localhost", ConvertToPort(myNeighbors[i]));
                    AddClient(c);
                }
            }

            //Thread.Sleep(6000);

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
                    //lock (locker)
                    //{
                    //    Nb[remotePort] = remotePort;
                    //    D[remotePort] = 1;
                    //    Console.WriteLine("/manually set Nb and D");
                    //}
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
                lock (DiscoveredNodes)
                {
                    if (!DiscoveredNodes[v] && dist != maxNodes)
                    {
                        DiscoveredNodes[v] = true;
                        cycleLimit++;
                        //Console.WriteLine("//found node {0}, nr of nodes is now {1}, value {2}", v, cycleLimit, dist);
                    }
                }
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
                SendMessage("OK,"+myPort, rp);
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
                for (int i = 0; i < maxNodes; i++)
                {
                    SendMyDist(remotePort, i, D[i]);
                    //Console.WriteLine("//sent md to: " + );
                }
        }

        private void ConnectionClosed(int remotePort)
        {
            connections[remotePort] = null;
            myNeighbors.Remove(remotePort);
            lock(messageQueues[remotePort])
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
            SendMessage(String.Format("myDist,{0},{1}", node, value),remotePort);
            //Console.WriteLine("//sent mydist to {0}: {1},{2}", remotePort, node, value);
        }

        private void SendTextMessage(int prefered, int destination, string message)
        {
            SendMessage(String.Format("message,{0},{1}", destination, message),prefered);
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
                    int d = int.MaxValue-1;
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
                    if ((D[remotePort] != CurrentDv || currNb != Nb[remotePort]) && D[remotePort] != maxNodes)
                        Console.WriteLine("Afstand naar {0} is nu {1} via {2}", ConvertToPort(remotePort), D[remotePort], ConvertToPort(Nb[remotePort])); //bestNb or Nb[remotePort]???
                    if (CurrentDv < maxNodes && D[remotePort] >= maxNodes)
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
