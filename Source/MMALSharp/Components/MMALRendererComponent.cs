﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Config;
using MMALSharp.Native;
using MMALSharp.Ports;
using MMALSharp.Ports.Inputs;
using static MMALSharp.MMALNativeExceptionHelper;

namespace MMALSharp.Components
{
    public abstract class MMALRendererBase : MMALDownstreamComponent
    {
        /// <summary>
        /// Create a new instance of a renderer component.
        /// </summary>
        /// <param name="name">The name of the component.</param>
        protected unsafe MMALRendererBase(string name) : base(name)
        {
            Inputs.Add(new InputPort((IntPtr)(&(*Ptr->Input[0])), this, Guid.NewGuid()));
        }
    }

    /// <summary>
    /// Represents a Null Sink component. This component should be used when a preview component is not required in order to measure exposure.
    /// </summary>
    public class MMALNullSinkComponent : MMALRendererBase
    {
        /// <summary>
        /// Creates a new instance of a Null Sink renderer component. This component is intended to be connected to the Camera's preview port
        /// and is used to measure exposure. No video preview is available with this renderer.
        /// </summary>
        public MMALNullSinkComponent() : base(MMALParameters.MMAL_COMPONENT_DEFAULT_NULL_SINK)
        {
            EnableComponent();
        }

        /// <inheritdoc />
        public override void PrintComponent()
        {
            MmalLog.Logger.LogInformation($"Component: Null sink renderer");
        }
    }

    /// <summary>
    /// Represents a Video Renderer component.
    /// </summary>
    public class MMALVideoRenderer : MMALRendererBase
    {
        /// <summary>
        /// Gets the configuration for this video renderer. Call <see cref="ConfigureRenderer"/> to apply changes.
        /// </summary>
        public PreviewConfiguration Configuration { get; }

        /// <summary>
        /// Gets a list of overlay renderers connected to this video renderer.
        /// </summary>
        public List<MMALOverlayRenderer> Overlays { get; } = new List<MMALOverlayRenderer>();

        /// <summary>
        /// Creates a new instance of a Video renderer component. This component is intended to be connected to the Camera's preview port
        /// and is used to measure exposure. It also produces real-time video to the Pi's HDMI output from the camera.
        /// </summary>
        public MMALVideoRenderer() : base(MMALParameters.MMAL_COMPONENT_DEFAULT_VIDEO_RENDERER)
        {
            EnableComponent();
        }

        /// <summary>
        /// Creates a new instance of a Video renderer component. This component is intended to be connected to the Camera's preview port
        /// and is used to measure exposure. It also produces real-time video to the Pi's HDMI output from the camera.
        /// </summary>
        /// <param name="config">The configuration object for this renderer.</param>
        public MMALVideoRenderer(PreviewConfiguration config) : base(MMALParameters.MMAL_COMPONENT_DEFAULT_VIDEO_RENDERER)
        {
            EnableComponent();
            Configuration = config;
        }

        /// <summary>
        /// Removes a <see cref="MMALOverlayRenderer"/> from this renderer's overlays if it exists.
        /// </summary>
        /// <param name="renderer">The overlay renderer to remove.</param>
        public void RemoveOverlay(MMALOverlayRenderer renderer)
        {
            Overlays.Remove(renderer);
            renderer.Dispose();
        }

        /// <summary>
        /// Commits all changes made to the configuration.
        /// </summary>
        /// <exception cref="MMALException"/>
        public unsafe void ConfigureRenderer()
        {
            if (Configuration == null)
                return;

            int fullScreen = 0, noAspect = 0, copyProtect = 0;
            uint displaySet = 0;

            MMAL_RECT_T? previewWindow = default(MMAL_RECT_T);

            if (!Configuration.FullScreen)
            {
                previewWindow = new MMAL_RECT_T(
                    Configuration.PreviewWindow.X, Configuration.PreviewWindow.Y,
                    Configuration.PreviewWindow.Width, Configuration.PreviewWindow.Height);
            }

            displaySet = (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_LAYER;
            displaySet |= (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_ALPHA;

            if (Configuration.FullScreen)
            {
                displaySet |= (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_FULLSCREEN;
                fullScreen = 1;
            }
            else
            {
                displaySet |= (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_DEST_RECT |
                              (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_FULLSCREEN;
            }

            if (Configuration.NoAspect)
            {
                displaySet |= (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_NOASPECT;
                noAspect = 1;
            }

            if (Configuration.CopyProtect)
            {
                displaySet |= (int)MMALParametersVideo.MMAL_DISPLAYSET_T.MMAL_DISPLAY_SET_COPYPROTECT;
                copyProtect = 1;
            }

            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MMAL_DISPLAYREGION_T>());

            MMAL_DISPLAYREGION_T displayRegion = new MMAL_DISPLAYREGION_T(
                new MMAL_PARAMETER_HEADER_T(
                    MMALParametersVideo.MMAL_PARAMETER_DISPLAYREGION,
                    Marshal.SizeOf<MMAL_DISPLAYREGION_T>()), displaySet, 0, fullScreen, Configuration.DisplayTransform, previewWindow ?? new MMAL_RECT_T(0, 0, 0, 0), new MMAL_RECT_T(0, 0, 0, 0), noAspect,
                    Configuration.DisplayMode, 0, 0, Configuration.Layer, copyProtect, Configuration.Opacity);

            Marshal.StructureToPtr(displayRegion, ptr, false);

            try
            {
                MMALCheck(MMALPort.mmal_port_parameter_set(Inputs[0].Ptr, (MMAL_PARAMETER_HEADER_T*)ptr), $"Unable to set preview renderer configuration");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <inheritdoc />
        public override void PrintComponent()
        {
            MmalLog.Logger.LogInformation($"Component: Video renderer");
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (GetType() == typeof(MMALVideoRenderer))
                Overlays.ForEach(c => c.Dispose());

            base.Dispose();
        }
    }

    /// <summary>
    /// MMAL provides the ability to add a static video render overlay onto the display output. The user must provide unencoded RGB input padded to the width/height of the camera block size (32x16).
    /// This class represents a video renderer which has the ability to overlay static resources to the display output.
    /// </summary>
    public sealed class MMALOverlayRenderer : MMALVideoRenderer
    {
        /// <summary>
        /// A reference to the current stream being used in the overlay.
        /// </summary>
        public byte[] Source { get; }

        /// <summary>
        /// The parent renderer which is being used to overlay onto the display.
        /// </summary>
        public MMALVideoRenderer ParentRenderer { get; }

        /// <summary>
        /// The configuration for rendering a static preview overlay.
        /// </summary>
        public PreviewOverlayConfiguration OverlayConfiguration { get; }

        /// <summary>
        /// A list of supported encodings for overlay image data.
        /// </summary>
        public readonly IReadOnlyCollection<MmalEncoding> AllowedEncodings = new ReadOnlyCollection<MmalEncoding>(new List<MmalEncoding>
        {
            MmalEncoding.I420,
            MmalEncoding.Rgb24,
            MmalEncoding.Rgba,
            MmalEncoding.Bgr24,
            MmalEncoding.BGra
        });

        /// <summary>
        /// Creates a new instance of a Overlay renderer component. This component is identical to the <see cref="MMALVideoRenderer"/> class, however it provides
        /// the ability to overlay a static source onto the render overlay.
        /// </summary>
        /// <param name="parent">The parent renderer which is being used to overlay onto the display.</param>
        /// <param name="config">The configuration for rendering a static preview overlay.</param>
        /// <param name="source">A reference to the current stream being used in the overlay.</param>
        public MMALOverlayRenderer(MMALVideoRenderer parent, PreviewOverlayConfiguration config, byte[] source) : base(config)
        {
            Source = source;
            ParentRenderer = parent;
            OverlayConfiguration = config;
            parent.Overlays.Add(this);

            if (config == null)
                return;

            var width = 0;
            var height = 0;

            if (config.Resolution.Width > 0 && config.Resolution.Height > 0)
            {
                width = config.Resolution.Width;
                height = config.Resolution.Height;
            }
            else
            {
                width = parent.Inputs[0].Resolution.Width;
                height = parent.Inputs[0].Resolution.Height;
            }

            if (config.Encoding == null)
            {
                var sourceLength = source.Length;
                var planeSize = Inputs[0].Resolution.Pad();
                var planeLength = Math.Floor((double)planeSize.Width * planeSize.Height);

                if (Math.Floor(sourceLength / planeLength) == 3)                
                    config.Encoding = MmalEncoding.Rgb24;                
                else if (Math.Floor(sourceLength / planeLength) == 4)                
                    config.Encoding = MmalEncoding.Rgba;                
                else                
                    throw new PiCameraError("Unable to determine encoding from image size.");                
            }

            if (!AllowedEncodings.Any(c => c.EncodingVal == Inputs[0].NativeEncodingType))            
                throw new PiCameraError($"Incompatible encoding type for use with Preview Render overlay {Inputs[0].NativeEncodingType.ParseEncoding().EncodingName}.");            

            var portConfig = new MMALPortConfig(
                config.Encoding,
                null,
                width: width,
                height: height);

            ConfigureInputPort(portConfig, null);

            Control.Start();
            Inputs[0].Start();
        }

        /// <summary>
        /// Updates the overlay by sending <see cref="Source"/> as new image data.
        /// </summary>
        public void UpdateOverlay()
        {
            UpdateOverlay(Source);
        }

        /// <summary>
        /// Updates the overlay by sending the specified buffer as new image data.
        /// </summary>
        /// <param name="imageData">Byte array containing the image data encoded like configured.</param>
        public void UpdateOverlay(byte[] imageData)
        {
            var buffer = Inputs[0].BufferPool.Queue.GetBuffer();

            if (buffer == null)
            {
                MmalLog.Logger.LogWarning("Received null buffer when updating overlay.");
                return;
            }

            buffer.ReadIntoBuffer(imageData, imageData.Length, false);
            Inputs[0].SendBuffer(buffer);
        }
    }
}