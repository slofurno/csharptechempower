using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace httplistener
{
  public class SliceManager
  {
    private byte[] _buffer;
    private int _count;
    private int _blockSize;
    private Stack<int> _freeBlocks;

    public SliceManager(int size, int count)
    {
      _freeBlocks = new Stack<int>();
      _buffer = new byte[size * count];

      for (int i = 0; i < count; i++)
      {
        _freeBlocks.Push(i*size);
      }

      _blockSize = size;
      _count = count;
    }

    public void setBuffer(SocketAsyncEventArgs args)
    {
      args.SetBuffer(_buffer, _freeBlocks.Pop(), _blockSize);

    }

  }



}
