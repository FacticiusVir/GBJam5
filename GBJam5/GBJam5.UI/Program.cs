﻿using GBJam5.Components;
using GBJam5.Services;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace GBJam5
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var game = new Game();

            const int windowWidth = 1024;
            const int windowHeight = 768;

            var hostForm = new Form()
            {
                Text = "GBJam 5",
                ClientSize = new Size(windowWidth, windowHeight)
            };

            var updateLoop = new UpdateLoopService();
            var windowHost = new WindowHostService();

            windowHost.WindowHandle = hostForm.Handle;
            windowHost.WindowWidth = windowWidth;
            windowHost.WindowHeight = windowHeight;

            hostForm.ClientSizeChanged += (x, y) =>
            {
                windowHost.WindowWidth = (uint)hostForm.ClientSize.Width;
                windowHost.WindowHeight = (uint)hostForm.ClientSize.Height;
                windowHost.SignalResize();
            };

            game.BindService<ILoggingService, DebugLoggingService>();
            game.BindService<IUpdateLoopService>(updateLoop);
            game.BindService<IWindowHostService>(windowHost);
            game.BindService<IEntityService, EntityService>();
            game.BindService<IKeyboardDeviceService, Win32KeyboardDeviceService>();
            game.BindService<IInputEventService, InputEventService>();
            game.BindService<IGraphicsDeviceService, VulkanDeviceService>();

            game.Initialise();

            game.Start();

            hostForm.Show();

            var entityManager = game.Services.GetService<IEntityService>();

            var manager = entityManager.CreateEntity();

            manager.AddComponent<PixelScaleManager>();

            var sprite = entityManager.CreateEntity();

            sprite.AddComponent<SpriteRenderer>();
            sprite.AddComponent<Transform2>();
            sprite.AddComponent<WasdMovement>();

            while (game.RunState == GameRunState.Running)
            {
                updateLoop.RunFrame();

                Application.DoEvents();
                
                if (hostForm.IsDisposed)
                {
                    game.SignalStop();
                }
            }

            game.Stop();
        }
    }
}
