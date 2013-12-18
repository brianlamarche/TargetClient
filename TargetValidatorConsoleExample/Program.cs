using ASMLTargetValidator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASMLTargetValidatorConsoleExample
{
    class Program
    {
        static void Main(string[] args)
        {
            // You have to set your IP and a port (4500 is the standard for this example)
            TargetWebServerValidator server = new TargetWebServerValidator();
            server.TargetHit    += server_TargetHit;
            server.IpAddress     = "10.0.0.2";
            server.Port          = 4500;

            // Create a thread for the server to live on.
            ThreadStart start = new ThreadStart(server.Start);
            Thread thread = new Thread(start);
            thread.Start();

            // then let's do something else...like wait.
            string data = Console.ReadLine();
            while (data.ToLower().Trim() != "q")
            {
                Console.WriteLine("Next: ");
                data = Console.ReadLine();

                if (data == "read")
                {
                    Console.WriteLine("target name: ");
                    string target = Console.ReadLine();

                    bool wasHit = server.WasTargetHit(target);
                    string modifier = "";
                    if (!wasHit)
                        modifier = "NOT";

                    Console.Write("Target: {0} was {1} hit", target, modifier);
                }
            }
        }

        // and we'll handle when the server tells us that a target was hit.
        static void server_TargetHit(object sender, TargetHitEventArgs e)
        {
            Console.WriteLine("Target HIT! {0} - {1}", e.TargetId, e.HitCount);
        }
    }
}
