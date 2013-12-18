using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using Fleck;
using ASMLEngineSdk;

namespace ASMLTargetValidator
{    
    [DataContract]
    class Game
    {
        [DataMember]
        public string code { get; set; }
        [DataMember]
        public long start { get; set; }
        [DataMember]
        public long end { get; set; }
        [DataMember]
        public string endDisplay { get; set; }
        [DataMember]
        public Target[] targets { get; set; }
        [DataMember]
        public int score { get; set; }
    }      
    [DataContract]
    class Team
    {
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string ip { get; set; }    
        [DataMember]
        public Game[] games { get; set; }               
    }   
    [DataContract]
    class Target
    {
        [DataMember]
        public int id { get; set; }
        [DataMember]
        public int status { get; set; }
        [DataMember]
        public int hit{ get; set; }
    }

    public class TargetHitEventArgs: EventArgs 
    {
        public TargetHitEventArgs(string targetId, int hitCount)
        {
            TargetId = targetId;
            HitCount = hitCount;
        }

        public string TargetId
        {
            get;
            private set;
        }
        public int HitCount
        {
            get;
            private set;
        }
    }

    /// <summary>
    /// Validates that a target was hit from a webserver
    /// </summary>
    public class TargetWebServerValidator: ITargetValidator
    {
        /// <summary>
        /// Fired when a target was hit.
        /// </summary>
        public event EventHandler<TargetHitEventArgs> TargetHit;

        /// <summary>
        /// Object for locking around the dictionary of hit counts
        /// </summary>
        private object m_threadSafeObject;
        /// <summary>
        /// Tracks how many times the target was hit.
        /// </summary>
        private Dictionary<string, int> m_targetHitCount;
        /// <summary>
        /// Flag indicating that we should be running in this thread.
        /// </summary>
        private volatile bool m_shouldRun = true;
        /// <summary>
        /// Event for synchronizing between the server and the context
        /// </summary>
        private ManualResetEvent m_stopServerEvent;

        /// <summary>
        /// Constructor.
        /// </summary>
        public TargetWebServerValidator()
        {
            IpAddress           = "10.0.0.4";
            Port                = 4500;
            m_shouldRun         = true;
            m_threadSafeObject  = new object();
            m_stopServerEvent   = new ManualResetEvent(false);
            m_targetHitCount    = new Dictionary<string, int>();
        }

        

        /// <summary>
        /// Gets or sets the IP address of this server.
        /// </summary>
        public string IpAddress { get; set; }
        /// <summary>
        /// Gets or sets the port of this web-server
        /// </summary>
        public int Port { get; set; }


        /// <summary>
        /// Stops the server from running.
        /// </summary>
        public void Stop()
        {
            m_shouldRun = false;
            m_stopServerEvent.Set();
        }

        /// <summary>
        /// Creates a blocking method call for the webserver.  This call should be threaded
        /// if you are running outside of this context.
        /// </summary>
        public void Start()
        {
            FleckLog.Level = LogLevel.Error;
            var allSockets = new List<IWebSocketConnection>();
            
            var server = new WebSocketServer(string.Format("ws://{0}:{1}", IpAddress, Port));
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    //Console.WriteLine("Open!");
                    allSockets.Add(socket);
                };
                socket.OnClose = () =>
                {
                    //Console.WriteLine("Close!");
                    allSockets.Remove(socket);
                };
                socket.OnMessage = message =>
                {
             
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Team));

                    byte[] byteArray = Encoding.UTF8.GetBytes(message);
                    Team team = null;
                    using (MemoryStream stream = new MemoryStream(byteArray))
                    {
                        team = serializer.ReadObject(stream) as Team;
                    }
                    if (team != null)
                    {
                        if (team.games != null && team.games.Length > 0)
                        {
                            List<Target> targetsHit = new List<Target>();

                            lock (m_threadSafeObject)
                            {
                                // Interrogate each target
                                foreach (Target target in team.games[team.games.Length - 1].targets)
                                {
                                    string id = target.id.ToString();
                                    if (target.hit > 0)
                                    {
                                        if (!m_targetHitCount.ContainsKey(id))
                                        {
                                            m_targetHitCount.Add(id, target.hit);
                                            targetsHit.Add(target);
                                            return;
                                        }
                                        else if (m_targetHitCount[id] < target.hit)
                                        {
                                            targetsHit.Add(target);
                                            m_targetHitCount[id] = target.hit;
                                        }
                                    }
                                    else
                                    {
                                        if (!m_targetHitCount.ContainsKey(id))
                                        {
                                            m_targetHitCount.Add(id, 0);
                                        }
                                        m_targetHitCount[id] = 0;
                                    }
                                }
                            }

                            // Now that we are out of the lock,
                            // we'll tell the user that the target was hit.
                            foreach (Target t in targetsHit)
                            {
                                OnTargetHit(t);
                            }
                        }
                        else
                        {
                            m_targetHitCount.Clear();
                        }
                        //allSockets.ToList().ForEach(s => s.Send("Success: " + message));
                    }
                    
                    Console.WriteLine();
                    Console.WriteLine("\tReceived: " + message);
                    Console.WriteLine();
                        
                };
            });
           
            // Synchronizes with the context.
            WaitHandle[] eventHandles = new WaitHandle[] { m_stopServerEvent };
            while (m_shouldRun)
            {
                // The 0th event is the event that we should stop.
                int dw = WaitHandle.WaitAny(eventHandles, 50);
                if (dw == 0)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Asks if the target was hit or not.
        /// </summary>
        /// <param name="name">Name of the target</param>
        /// <returns>True if hit, False if not hit</returns>
        public bool WasTargetHit(string name)
        {
            bool wasHit = false;
            lock (m_threadSafeObject)
            {
                if (m_targetHitCount.ContainsKey(name))
                {
                    wasHit = m_targetHitCount[name] > 0;                                            
                }
            }
            return wasHit;
        }

        /// <summary>
        /// Allows us to validate that the event was subscribed to.
        /// </summary>
        /// <param name="target"></param>
        private void OnTargetHit(Target target)
        {
            if (this.TargetHit != null)
            {
                string id = target.id.ToString();
                TargetHit(this, new TargetHitEventArgs(id, target.hit));
            }
        }

        /// <summary>
        /// Resets the hit count tracking
        /// </summary>
        public void Reset()
        {
            lock (m_threadSafeObject)
            {
                m_targetHitCount.Clear();
            }
        }
    }

}
