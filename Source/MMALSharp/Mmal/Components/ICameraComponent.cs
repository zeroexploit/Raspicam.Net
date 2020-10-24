﻿using MMALSharp.Mmal.Handlers;
using MMALSharp.Mmal.Ports.Outputs;

namespace MMALSharp.Mmal.Components
{
    interface ICameraComponent : IComponent
    {
        IOutputPort PreviewPort { get; }
        IOutputPort VideoPort { get; }
        IOutputPort StillPort { get; }
        ICameraInfoComponent CameraInfo { get; }

        void Initialise(ICaptureHandler stillCaptureHandler = null, ICaptureHandler videoCaptureHandler = null);
    }
}