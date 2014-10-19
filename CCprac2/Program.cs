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
        //max number of nodes in the network (hardcode)
        const int maxNodes = 20;

        //lowest port number in use (hardcode)
        const int portLowerBound = 55500;

        //my port nr (internal)
        public static int myPort;

        //list with ports of neighbors
        List<int> myNeighbors;

        //array of connections to neighbors
        public Client[] connections;

        //message buffers for each neighbor
        public Queue<string>[] messageQueues;

        //thread blockers for each connections
        AutoResetEvent[] waiter;
        public static ManualResetEvent blocker;

        //limit on which nodes are deemed unreachable
        int cycleLimit;
        bool[] discoveredNodes;

        //locking objects
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

            //create an array to accomodate for a connection to each port + buffers and blockers.
            connections = new Client[maxNodes];
            waiter = new AutoResetEvent[maxNodes];
            messageQueues = new Queue<string>[maxNodes];
            for (int i = 0; i < maxNodes; i++)
            {
                messageQueues[i] = new Queue<string>();
                waiter[i] = new AutoResetEvent(false);
            }

            blocker = new ManualResetEvent(false);

            //set the limit to known number of nodes
            discoveredNodes = new bool[maxNodes];
            for (int i = 0; i < maxNodes; i++) { discoveredNodes[i] = false; }
            for (int i = 0; i < myNeighbors.Count; i++)
            {
                discoveredNodes[myNeighbors[i]] = true;                
            }
            discoveredNodes[myPort] = true;
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

            //send all initial mydists
            SendInit();

            //release all readers
            blocker.Set();

            //start reading console input
            ReadConsoleInput();
        }

        #region Input Handling

        /// <summary>
        /// Reads input from the console
        /// </summary>
        private void ReadConsoleInput()
        {
            while (true)
            {
                string[] s = Console.ReadLine().Split();
                if (s[0] == "B") //send a message to given destination
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));
                    string message = string.Join(" ", s.Skip(2));
                    if (remotePort == myPort) //if its to me: echo
                    {
                        Console.WriteLine(message);
                    }
                    else if (Nb[remotePort] != -1) //if not to me: send te preferred neighbor
                    {
                        SendTextMessage(Nb[remotePort], remotePort, message);
                    }
                    else //if unknown destination: error
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", ConvertToPort(remotePort)));
                }
                else if (s[0] == "D") //disconnect from given neighbor
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));
                    if (Nb[remotePort] != -1)
                        connections[remotePort].Disconnect(Client.DisconnectReason.Command);
                    else
                        Console.WriteLine(string.Format("Poort {0} is niet bekend", ConvertToPort(remotePort)));
                }
                else if (s[0] == "C") //connect to given node
                {
                    int remotePort = ConvertFromPort(int.Parse(s[1]));

                    //make tcp connection
                    TcpClient c = new TcpClient("localhost", ConvertToPort(remotePort));

                    //add the client
                    AddClient(c);

                    //add the new neighbor to the list of neighbors
                    myNeighbors.Add(remotePort);

                    //notify other node of new connection
                    SendMessage("new," + myPort, remotePort);

                    //wait for confirmation from other node.
                    waiter[remotePort].WaitOne();

                    //send routing data to new connection
                    NewConnection(remotePort);
                }
                else if (s[0] == "R") //print the routing table;
                {
                    PrintTable();
                }
            }
        }

        /// <summary>
        /// prints the routing table
        /// </summary>
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

        /// <summary>
        /// Handles incoming messages from all connections
        /// </summary>
        /// <param name="message">the message recieved</param>
        /// <param name="remotePort">the port from which the message originated</param>
        private void HandleMessage(string message, int remotePort)
        {
            string[] mParts = message.Split(',');
            if (mParts[0] == "myDist")
            {
                //the destination in the MD
                int v = int.Parse(mParts[1]);

                //the distance in the MD
                int dist = int.Parse(mParts[2]);

                //Console.WriteLine("//recieved MD dest: {0}, dist: {1}, from: {2}",v,dist,remotePort);
                
                //set ndis
                ndis[remotePort][v] = dist;

                //check if the cycle limit is still correct, if not update and recompute previously deemed unreachable nodes
                lock (cycleLock)
                {
                    if (!discoveredNodes[v] && dist < maxNodes)
                    {
                        discoveredNodes[v] = true;
                        cycleLimit++;
                        Console.WriteLine("//found {0}, cycle limit is now {1}", v, cycleLimit);
                        for (int i = 0; i < maxNodes; i++)
                        {
                            if (rejectedOn[i] < cycleLimit)
                                Recompute(i);
                        }
                    }
                }

                Recompute(v);
            }
            else if (mParts[0] == "message") //if its a text message
            {
                //get the destiantion from the message
                int destination = int.Parse(mParts[1]);

                if (destination == myPort) // if its for me: echo
                {
                    Console.WriteLine(mParts[2]);
                }
                else //if not for me: redirect
                {
                    int nextHop = Nb[destination];
                    SendTextMessage(nextHop, destination, mParts[2]);
                    Console.WriteLine("Bericht voor {0} doorgestuurd naar {1}", ConvertToPort(destination), ConvertToPort(nextHop));
                }
            }
            else if (mParts[0] == "new") //message is a signal notifying of new connection;
            {
                //get the remote host nr
                int rp = int.Parse(mParts[1]);

                //add new neighbor to list of neighbors
                myNeighbors.Add(rp);

                //send confirmation of addition to new neighbor
                SendMessage("OK," + myPort, rp);

                //send routing info to new neighbor
                NewConnection(rp);
            }
            else if (mParts[0] == "OK") //message is connection confirmation
            {
                //unblock thread
                waiter[int.Parse(mParts[1])].Set();
            }
            else
            {
                Console.WriteLine("//unknown message type");
            }
        }

        /// <summary>
        /// makes a client wrapper, adds it to the array of clients, and gives the client it buffer
        /// </summary>
        /// <param name="socket">the tcp-socket of the new connection</param>
        public void AddClient(TcpClient socket)
        {
            Client client = new Client(socket, HandleMessage, ConnectionClosed);
            connections[client.RemotePort] = client;
            client.SetQueue(messageQueues[client.RemotePort]);
        }

        /// <summary>
        /// sends all estimated distances to the new neighbor in order from closest by to furthest away
        /// </summary>
        /// <param name="remotePort"></param>
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

        /// <summary>
        /// handles the closing of a connection
        /// </summary>
        /// <param name="remotePort">the nr of the connection being closed</param>
        private void ConnectionClosed(int remotePort)
        {
            //remove connection from array
            connections[remotePort] = null;

            //remove neighbor from list
            myNeighbors.Remove(remotePort);

            //clear the buffer
            lock (messageQueues[remotePort])
            {
                messageQueues[remotePort].Clear();
            }

            //clear the ndis records for this neighbor and recompute all destination having the closed connection as preferred
            for (int v = 0; v < maxNodes; v++)
            {
                ndis[remotePort][v] = maxNodes;
                if (Nb[v] == remotePort)
                    Recompute(v);
            }
        }

        /// <summary>
        /// Sends a mydist
        /// </summary>
        /// <param name="remotePort">mydist destination</param>
        /// <param name="node">the node in question</param>
        /// <param name="value">my distance to the node in question</param>
        private void SendMyDist(int remotePort, int node, int value)
        {
            SendMessage(String.Format("myDist,{0},{1}", node, value), remotePort);
        }

        /// <summary>
        /// Sends a text message
        /// </summary>
        /// <param name="prefered">the next hop</param>
        /// <param name="destination">the destination</param>
        /// <param name="message">the message</param>
        private void SendTextMessage(int prefered, int destination, string message)
        {
            SendMessage(String.Format("message,{0},{1}", destination, message), prefered);
        }

        /// <summary>
        /// sends a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="remotePort">the destination</param>
        private void SendMessage(string message, int remotePort)
        {
            //add the message to the buffer
            lock (messageQueues[remotePort])
            {
                messageQueues[remotePort].Enqueue(message);
            }
            //if a connection has been made: notify it of a new message in the buffer
            if (connections[remotePort] != null)
                connections[remotePort].SendMessage();
        }

        #endregion

        #region Port Conversion

        /// <summary>
        /// converts from external port to internal port
        /// </summary>
        /// <param name="port">external port</param>
        /// <returns>internal port</returns>
        public static int ConvertFromPort(int port)
        {
            return port - portLowerBound;
        }

        /// <summary>
        /// converts from internal port to external port
        /// </summary>
        /// <param name="index">internal port</param>
        /// <returns>external port</returns>
        public static int ConvertToPort(int index)
        {
            return index + portLowerBound;
        }

        #endregion

        #region Routing

        //netchange routing information
        int[][] ndis = new int[maxNodes][];
        int[] D = new int[maxNodes];
        int[] Nb = new int[maxNodes];

        //table with rejection limits
        int[] rejectedOn = new int[maxNodes];

        /// <summary>
        /// sets all routing information to its default values
        /// </summary>
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

        /// <summary>
        /// sends the initial mydists
        /// </summary>
        private void SendInit()
        {
            Parallel.For(0, myNeighbors.Count, (iterator) =>
            {
                SendMyDist(myNeighbors[iterator], myPort, 0);
            });
        }

        /// <summary>
        /// recomputes if there is a better path to a node
        /// </summary>
        /// <param name="remotePort">the node to recompute</param>
        private void Recompute(int remotePort)
        {
            lock (locker)
            {
                //gets the current estimated distance, sets the best found neighbor to nothing, and gets the current neighbor
                int CurrentDv = D[remotePort], bestNb = -1, currNb = Nb[remotePort];


                if (remotePort == myPort) //if the port to recompute is my own port
                {
                    D[remotePort] = 0;
                    Nb[remotePort] = remotePort;
                }
                else //if its not my port
                {
                    //set best found distance to maxInt (-1 to fix a bug)
                    int d = int.MaxValue - 1;

                    //value depicting if a node can be reached
                    bool reachable;

                    //get the shortest nr of hops to the node, and the next hop on this path
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
                        if (d < cycleLimit) //if the node can be reached: set the distance and next hop 
                        {
                            D[remotePort] = d;
                            Nb[remotePort] = bestNb;

                            reachable = true;

                            //set this not to not rejected
                            rejectedOn[remotePort] = int.MaxValue;
                        }
                        else //if the node cannot be reached: set the distance to max and next hop to undefined
                        {
                            D[remotePort] = maxNodes;
                            Nb[remotePort] = -1;

                            reachable = false;

                            //set this not to rejected, the value indicates the distance limit
                            rejectedOn[remotePort] = cycleLimit;
                        } 
                    }

                    //if the distance to a node or the next hop have changed and the node is still reachable, notify
                    if ((D[remotePort] != CurrentDv || currNb != Nb[remotePort]) && reachable)
                        Console.WriteLine("Afstand naar {0} is nu {1} via {2}", ConvertToPort(remotePort), D[remotePort], ConvertToPort(Nb[remotePort]));
        
                    //if the node cannot be reached: notify
                    if (!reachable)
                        Console.WriteLine("Onbereikbaar: " + ConvertToPort(remotePort));
                }

                //if the distance to a node has changed: send MD's to all neighbors
                if (D[remotePort] != CurrentDv)
                {
                    Parallel.For(0, myNeighbors.Count, (iterator) =>
                    {
                        SendMyDist(myNeighbors[iterator], remotePort, D[remotePort]);
                    });
                    //Console.WriteLine("//sent MD's for port {0} and dist {1}",remotePort, D[remotePort]);
                }
            }
        }

        #endregion
    }
}
