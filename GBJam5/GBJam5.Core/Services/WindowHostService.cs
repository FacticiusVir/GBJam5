using System;

namespace GBJam5.Services
{
    public class WindowHostService
        : GameService, IWindowHostService, IUpdatable
    {
        private IUpdateLoopService updateLoop;

        private bool isResizeSignalled;

        public bool IsResized
        {
            get;
            private set;
        }

        public IntPtr WindowHandle
        {
            get;
            set;
        }

        public uint WindowWidth
        {
            get;
            set;
        }

        public uint WindowHeight
        {
            get;
            set;
        }

        public void SignalResize()
        {
            this.isResizeSignalled = true;
        }

        public override void Initialise(Game game)
        {
            this.updateLoop = game.Services.GetService<IUpdateLoopService>();
        }

        public override void Start()
        {
            this.updateLoop.Register(this, UpdateStage.PreRender);
        }

        public void Update()
        {
            this.IsResized = this.isResizeSignalled;

            this.isResizeSignalled = false;
        }

        public override void Stop()
        {
            this.updateLoop.Deregister(this);
        }
    }

    public interface IWindowHostService
        : IGameService
    {
        IntPtr WindowHandle { get; }

        uint WindowWidth { get; }

        uint WindowHeight { get; }

        bool IsResized { get; }
    }
}