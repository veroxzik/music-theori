﻿using theori.Graphics;

namespace theori.BootLoaders
{
    public sealed class SharedBootLoader : Layer
    {
        private readonly BasicSpriteRenderer m_renderer = new BasicSpriteRenderer();

        public SharedBootLoader()
        {
        }

        public override void Render()
        {
            m_renderer.BeginFrame();
            m_renderer.Write("Shared Boot Loader, 日本語です", 10, 10);
            m_renderer.EndFrame();
        }
    }
}
