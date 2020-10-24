﻿using MMALSharp.Mmal.Callbacks;
using MMALSharp.Mmal.Components;
using MMALSharp.Mmal.Ports.Inputs;
using MMALSharp.Mmal.Ports.Outputs;

namespace MMALSharp.Mmal
{
    interface IConnection : IMmalObject
    {
        IConnectionCallbackHandler CallbackHandler { get; }
        IDownstreamComponent DownstreamComponent { get; }
        IComponent UpstreamComponent { get; }
        IInputPort InputPort { get; }
        IOutputPort OutputPort { get; }
        IBufferPool ConnectionPool { get; set; }
        string Name { get; }
        bool Enabled { get; }
        uint Flags { get; }
        long TimeSetup { get; }
        long TimeEnable { get; }
        long TimeDisable { get; }

        void Enable();
        void Disable();
        void Destroy();

        void RegisterCallbackHandler(IConnectionCallbackHandler handler);
    }
}