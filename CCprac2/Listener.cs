using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetwProg
{
    class Listener
    {
        TcpListener server;
        public void Listen(object p)
        {
            //start a new listener
            server = new TcpListener(IPAddress.Any, Program.ConvertToPort(Program.myPort));
            server.Start();
            Console.WriteLine("//listening on port " + Program.ConvertToPort(Program.myPort));

            try
            {
                //listen for incoming connections indefinetly
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
