﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp.Config;
using MMALSharp.Mmal.Callbacks;
using MMALSharp.Mmal.Components;
using MMALSharp.Mmal.Handlers;
using MMALSharp.Mmal.Ports.Inputs;
using MMALSharp.Native.Buffer;
using MMALSharp.Native.Port;
using MMALSharp.Utility;

namespace MMALSharp.Mmal.Ports.Outputs
{
    unsafe class VideoPort : OutputPort, IVideoPort
    {
        public override Resolution Resolution
        {
            get => new Resolution(Width, Height);
            internal set
            {
                if (value.Width == 0 || value.Height == 0)
                {
                    Width = CameraConfig.Resolution.Pad().Width;
                    Height = CameraConfig.Resolution.Pad().Height;
                }
                else
                {
                    Width = value.Pad().Width;
                    Height = value.Pad().Height;
                }
            }
        }

        public VideoPort(IntPtr ptr, IComponent comp, Guid guid) : base(ptr, comp, guid) { }

        public VideoPort(IPort copyFrom) : base((IntPtr)copyFrom.Ptr, copyFrom.ComponentReference, copyFrom.Guid) { }

        public override void Configure(IMmalPortConfig config, IInputPort copyFrom, ICaptureHandler handler)
        {
            base.Configure(config, copyFrom, handler);

            CallbackHandler = new VideoOutputCallbackHandler(this, (ICaptureHandler)handler, config.Split, config.StoreMotionVectors);
        }
        
        internal override void NativeOutputPortCallback(MmalPortType* port, MmalBufferHeader* buffer)
        {
            if (CameraConfig.Debug)
                MmalLog.Logger.LogDebug($"{Name}: In native {nameof(VideoPort)} output callback");

            var bufferImpl = new MmalBuffer(buffer);

            bufferImpl.PrintProperties();

            var eos = (PortConfig.Timeout.HasValue && DateTime.Now.CompareTo(PortConfig.Timeout.Value) > 0) || ComponentReference.ForceStopProcessing;

            if (bufferImpl.CheckState() && bufferImpl.Length > 0 && !eos && !Trigger.Task.IsCompleted)
                CallbackHandler.Callback(bufferImpl);

            // Ensure we release the buffer before any signalling or we will cause a memory leak due to there still being a reference count on the buffer.
            ReleaseBuffer(bufferImpl, eos);

            if (eos && !Trigger.Task.IsCompleted)
            {
                MmalLog.Logger.LogDebug($"{Name}: Timeout exceeded, triggering signal.");
                Task.Run(() => { Trigger.SetResult(true); });
            }
        }
    }
}