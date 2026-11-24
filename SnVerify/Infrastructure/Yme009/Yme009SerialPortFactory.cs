using System;
using System.IO.Ports;
using SnVerify.Services.Yme009;

namespace SnVerify.Infrastructure.Yme009
{
    /// <summary>
    /// 短生命周期串口。115200 8N1 只是打开端口所需的参考参数，不是已确认的 YME009 协议。
    /// </summary>
    public sealed class Yme009SerialPortFactory : IYme009SerialPortFactory
    {
        public IYme009SerialPortSession Open(string portName)
        {
            return new Session(portName);
        }

        private sealed class Session : IYme009SerialPortSession
        {
            private readonly SerialPort _port;
            private bool _disposed;

            public Session(string portName)
            {
                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                _port.ReadTimeout = 200;
                _port.WriteTimeout = 2000;
                _port.DtrEnable = true;
                _port.RtsEnable = true;
                _port.Open();
            }

            public void Write(string text)
            {
                EnsureOpen();
                _port.Write(text ?? string.Empty);
            }

            public string ReadExisting()
            {
                EnsureOpen();
                if (_port.BytesToRead <= 0)
                    return string.Empty;

                return _port.ReadExisting();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                try
                {
                    if (_port.IsOpen)
                        _port.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("YME009 串口关闭失败: " + ex.Message);
                }

                _port.Dispose();
            }

            private void EnsureOpen()
            {
                if (_disposed)
                    throw new ObjectDisposedException("Yme009SerialPort");
            }
        }
    }
}
