using System.Collections.Generic;
using System.Windows.Forms;

namespace GBJam5.Services
{
    public class Win32KeyboardDeviceService
        : GameService, IKeyboardDeviceService, IUpdatable, IMessageFilter
    {
        private IUpdateLoopService updateLoop;
        private List<KeyEvent> keyEvents = new List<KeyEvent>();

        private HashSet<Keys> keysDown = new HashSet<Keys>();
        private HashSet<Keys> newKeysDown = new HashSet<Keys>();

        public override void Initialise(Game game)
        {
            this.updateLoop = game.Services.GetService<IUpdateLoopService>();
        }

        public override void Start()
        {
            this.updateLoop.Register(this, UpdateStage.PreUpdate);

            Application.AddMessageFilter(this);
        }

        public void Update()
        {
            this.newKeysDown.Clear();

            for (int eventIndex = 0; eventIndex < this.keyEvents.Count; eventIndex++)
            {
                KeyEvent keyEvent = this.keyEvents[eventIndex];

                if (keyEvent.IsKeyDown)
                {
                    this.newKeysDown.Add(keyEvent.Key);
                    this.keysDown.Add(keyEvent.Key);
                }
                else
                {
                    this.keysDown.Remove(keyEvent.Key);
                }
            }

            this.keyEvents.Clear();
        }

        public override void Stop()
        {
            Application.RemoveMessageFilter(this);

            this.updateLoop.Deregister(this);
        }

        public bool IsKeyDown(Keys key)
        {
            return this.keysDown.Contains(key);
        }

        public bool OnKeyDown(Keys key)
        {
            return this.newKeysDown.Contains(key);
        }

        public bool PreFilterMessage(ref Message m)
        {
            Keys key = (Keys)m.WParam.ToInt32() & Keys.KeyCode;

            if (m.Msg == (int)WM.KEYDOWN)
            {
                int previousState = m.LParam.ToInt32() & 1 << 30;

                if (previousState == 0)
                {
                    this.keyEvents.Add(new KeyEvent
                    {
                        IsKeyDown = true,
                        Key = key
                    });
                }
            }
            else if (m.Msg == (int)WM.KEYUP)
            {
                this.keyEvents.Add(new KeyEvent
                {
                    IsKeyDown = false,
                    Key = key
                });
            }

            return false;
        }

        private struct KeyEvent
        {
            public bool IsKeyDown;
            public Keys Key;
        }
    }
}
