﻿using System;
using System.Text;
using MMALSharp.Common;
using MMALSharp.Processing;

namespace MMALSharp.Processors
{
    /// <summary>
    /// The BayerMetaProcessor is used to strip Bayer metadata from a JPEG image frame via the Image Processing API.
    /// </summary>
    public class BayerMetaProcessor : IFrameProcessor
    {
        /// <summary>
        /// The camera version being used.
        /// </summary>
        public CameraVersion CameraVersion { get; }

        /// <summary>
        /// The length of the metadata for the OmniVision OV5647.
        /// </summary>
        public const int BayerMetaLengthV1 = 6404096;

        /// <summary>
        /// The length of the metadata for the Sony IMX219.
        /// </summary>
        public const int BayerMetaLengthV2 = 10270208;
        
        /// <summary>
        /// Initialises a new instance of <see cref="BayerMetaProcessor"/>.
        /// </summary>
        /// <param name="camVersion">The camera version you're using.</param>
        public BayerMetaProcessor(CameraVersion camVersion)
        {
            CameraVersion = camVersion;
        }

        /// <inheritdoc />
        public void Apply(ImageContext context)
        {
            byte[] array = null;
            
            switch (CameraVersion)
            {
                case CameraVersion.Ov5647:
                    array = new byte[BayerMetaLengthV1];
                    Array.Copy(context.Data, context.Data.Length - BayerMetaLengthV1, array, 0, BayerMetaLengthV1);
                    break;
                case CameraVersion.Imx219:
                    array = new byte[BayerMetaLengthV2];
                    Array.Copy(context.Data, context.Data.Length - BayerMetaLengthV2, array, 0, BayerMetaLengthV2);
                    break;
            }

            byte[] meta = new byte[4];
            Array.Copy(array, 0, meta, 0, 4);

            if (Encoding.ASCII.GetString(meta) != "BRCM")            
                throw new Exception("Could not find Bayer metadata in header");            
            
            context.Data = new byte[array.Length];
            Array.Copy(array, context.Data, array.Length);
        }
    }
}
