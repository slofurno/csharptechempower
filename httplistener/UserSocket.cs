using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace httplistener
{
  class UserSocket
  {
    public Socket Socket { get; set; }
    public int Read { get; set; }
    public string Path { get; set; }

    public UserSocket()
    {

    }

    public UserSocket(Socket socket)
    {
      this.Socket = socket;
    }


  }
}
