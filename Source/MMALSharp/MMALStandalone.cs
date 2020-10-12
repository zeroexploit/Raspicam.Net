using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Native;

namespace MMALSharp
{
    public class MMALStandalone
    {
        public static MMALStandalone Instance => Lazy.Value;

        static readonly Lazy<MMALStandalone> Lazy = new Lazy<MMALStandalone>(() => new MMALStandalone());

        MMALStandalone()
        {
            BcmHost.bcm_host_init();
        }

        public async Task ProcessAsync(IDownstreamComponent initialComponent, CancellationToken cancellationToken = default)
        {
            var handlerComponents = PopulateProcessingList(initialComponent);

            initialComponent.Control.Start();
            initialComponent.Inputs[0].Start();

            var tasks = new List<Task>
            {
                initialComponent.Inputs[0].Trigger.Task
            };

            // Enable all connections associated with these components
            foreach (var component in handlerComponents)
            {
                component.EnableConnections();
                component.ForceStopProcessing = false;

                foreach (var port in component.ProcessingPorts.Values)
                {
                    if (port.ConnectedReference != null)
                        continue;

                    port.Start();
                    tasks.Add(port.Trigger.Task);
                }
            }

            // Get buffer from input port pool                
            var inputBuffer = initialComponent.Inputs[0].BufferPool.Queue.GetBuffer();

            if (inputBuffer.CheckState())
                initialComponent.Inputs[0].SendBuffer(inputBuffer);

            if (cancellationToken == CancellationToken.None)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(-1, cancellationToken)).ConfigureAwait(false);

                foreach (var component in handlerComponents)
                    component.ForceStopProcessing = true;

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            // Cleanup each downstream component.
            foreach (var component in handlerComponents)
            {
                foreach (var port in component.ProcessingPorts.Values.Where(p => p.ConnectedReference == null))
                    port.DisablePort();

                component.CleanPortPools();
                component.DisableConnections();
            }
        }

        public void PrintPipeline(IDownstreamComponent initialComponent)
        {
            MmalLog.Logger.LogInformation("Current pipeline:");
            MmalLog.Logger.LogInformation(string.Empty);

            foreach (var component in PopulateProcessingList(initialComponent))
                component.PrintComponent();
        }

        public void Cleanup()
        {
            MmalLog.Logger.LogDebug("Destroying final components");

            var tempList = new List<MMALDownstreamComponent>(MMALBootstrapper.DownstreamComponents);

            tempList.ForEach(c => c.Dispose());

            BcmHost.bcm_host_deinit();
        }

        List<IDownstreamComponent> PopulateProcessingList(IDownstreamComponent initialComponent)
        {
            var list = new List<IDownstreamComponent>();

            if (initialComponent != null)
                FindComponents(initialComponent, list);

            return list;
        }

        void FindComponents(IDownstreamComponent downstream, List<IDownstreamComponent> list)
        {
            if (downstream.Outputs.Count == 0)
                return;

            if (downstream.Outputs.Count == 1 && downstream.Outputs[0].ConnectedReference == null)
            {
                list.Add(downstream);
                return;
            }

            if (downstream is IDownstreamHandlerComponent checkDownstream)
                list.Add((IDownstreamHandlerComponent)downstream);

            foreach (var output in downstream.Outputs.Where(o => o.ConnectedReference != null))
                FindComponents(output.ConnectedReference.DownstreamComponent, list);
        }
    }
}