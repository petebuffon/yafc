using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using SDL2;

namespace YAFC.UI
{
    public abstract class Window : WidgetContainer, IPanel
    {
        public readonly UiBatch rootBatch;
        private IntPtr window;
        internal IntPtr renderer;
        internal IntPtr surface;
        public uint id { get; private set; }
        private float contentWidth, contentHeight;
        private int windowWidth, windowHeight;
        private bool repaintRequired = true;
        private bool software;
        public bool visible { get; private set; }
        private Window parent;
        internal long nextRepaintTime = long.MaxValue;

        public override SchemeColor boxColor => SchemeColor.Background;

        public virtual RectAllocator defaultAllocator => RectAllocator.Stretch;

        protected Window()
        {
            padding = new Padding(5f, 2f);
            rootBatch = new UiBatch(this);
        }

        private float UnitsToPixelsFromDpi(float dpi) => dpi == 0 ? 13 : MathUtils.Round(dpi / 6.8f);

        private float unitsToPixels;

        protected void Create(string title, float width, bool software, Window parent)
        {
            if (visible)
                return;
            this.software = software;
            this.parent = parent;
            contentWidth = width;
            var display = parent == null ? 0 : SDL.SDL_GetWindowDisplayIndex(parent.window);
            SDL.SDL_GetDisplayDPI(display, out var ddpi, out _, out _);
            unitsToPixels = UnitsToPixelsFromDpi(ddpi);
            rootBatch.Rebuild(this, new SizeF(contentWidth, 100f), unitsToPixels);
            windowWidth = rootBatch.UnitsToPixels(contentWidth);
            windowHeight = rootBatch.UnitsToPixels(contentHeight);
            var flags = (SDL.SDL_WindowFlags) 0;
            if (parent != null)
                flags |= SDL.SDL_WindowFlags.SDL_WINDOW_UTILITY | SDL.SDL_WindowFlags.SDL_WINDOW_SKIP_TASKBAR;
            if (!software)
                flags |= SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL;
            window = SDL.SDL_CreateWindow(title,
                SDL.SDL_WINDOWPOS_CENTERED_DISPLAY(display),
                SDL.SDL_WINDOWPOS_CENTERED_DISPLAY(display),
                windowWidth,
                windowHeight,
                flags
            );

            if (software)
            {
                surface = SDL.SDL_GetWindowSurface(window);
                renderer = SDL.SDL_CreateSoftwareRenderer(surface);
            }
            else
            {
                renderer = SDL.SDL_CreateRenderer(window, 0, SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
            }

            SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
            id = SDL.SDL_GetWindowID(window);
            Ui.RegisterWindow(id, this);
            visible = true;
        }

        internal void WindowResize()
        {
            if (software)
            {
                surface = SDL.SDL_GetWindowSurface(window);
                renderer = SDL.SDL_CreateSoftwareRenderer(surface);
                SDL.SDL_SetRenderDrawBlendMode(renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
            }
            rootBatch.Rebuild();
        }

        internal void WindowMoved()
        {
            var index = SDL.SDL_GetWindowDisplayIndex(window);
            SDL.SDL_GetDisplayDPI(index, out var ddpi, out _, out _);
            var u2p = UnitsToPixelsFromDpi(ddpi);
            if (u2p != unitsToPixels)
            {
                unitsToPixels = u2p;
                repaintRequired = true;
                rootBatch.MarkEverythingForRebuild();
            }
        }

        public void Render()
        {
            if (!repaintRequired && nextRepaintTime > Ui.time)
                return;
            nextRepaintTime = long.MaxValue;
            repaintRequired = false;
            if (rootBatch.IsRebuildRequired())
                rootBatch.Rebuild(this, new SizeF(contentWidth, 100f), unitsToPixels);
            var bgColor = boxColor.ToSdlColor();
            SDL.SDL_SetRenderDrawColor(renderer, bgColor.r,bgColor.g,bgColor.b, bgColor.a);
            SDL.SDL_RenderClear(renderer);
            
            var newWindowWidth = rootBatch.UnitsToPixels(contentWidth);
            var newWindowHeight = rootBatch.UnitsToPixels(contentHeight);
            if (windowWidth != newWindowWidth || windowHeight != newWindowHeight)
            {
                windowWidth = newWindowWidth;
                windowHeight = newWindowHeight;
                SDL.SDL_SetWindowSize(window, newWindowWidth, newWindowHeight);
                WindowResize();
            }
            
            rootBatch.Present(this, default, new RectangleF(default, new SizeF(contentWidth, contentHeight)));
            SDL.SDL_RenderPresent(renderer);
            if (surface != IntPtr.Zero)
            {
                SDL.SDL_UpdateWindowSurface(window);
            }
        }

        public bool Raycast<T>(PointF position, out T result, out UiBatch batch) where T : class, IMouseHandle => rootBatch.Raycast<T>(position, out result, out batch);

        public void BuildPanel(LayoutState state)
        {
            Build(state);
            contentHeight = state.fullHeight;
        }

        internal void DrawIcon(SDL.SDL_Rect position, Icon icon, SchemeColor color)
        {
            if (software)
            {
                var sdlColor = color.ToSdlColor();
                var iconSurface = IconCollection.GetIconSurface(icon);
                SDL.SDL_SetSurfaceColorMod(iconSurface, sdlColor.r, sdlColor.g, sdlColor.b);
                SDL.SDL_BlitScaled(iconSurface, ref IconCollection.IconRect, surface, ref position);
            }
        }

        internal void DrawBorder(SDL.SDL_Rect position, RectangleBorder border)
        {
            if (software)
            {
                int shadowTop, shadowSide, shadowBottom;
                if (border == RectangleBorder.Full)
                {
                    shadowTop = MathUtils.Round(unitsToPixels * 0.5f);
                    shadowSide = MathUtils.Round(unitsToPixels);
                    shadowBottom = MathUtils.Round(unitsToPixels * 2f);
                }
                else
                {
                    shadowTop = MathUtils.Round(unitsToPixels * 0.2f);
                    shadowSide = MathUtils.Round(unitsToPixels * 0.3f);
                    shadowBottom = MathUtils.Round(unitsToPixels * 0.5f);
                }
                var rect = new SDL.SDL_Rect {h = shadowTop, x = position.x - shadowSide, y = position.y-shadowTop, w = shadowSide};
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleTopLeft, surface, ref rect);
                rect.x = position.x;
                rect.w = position.w;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleTop, surface, ref rect);
                rect.x += rect.w;
                rect.w = shadowSide;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleTopRight, surface, ref rect);
                rect.y = position.y;
                rect.h = position.h;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleRight, surface, ref rect);
                rect.y += rect.h;
                rect.h = shadowBottom;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleBottomRight, surface, ref rect);
                rect.x = position.x;
                rect.w = position.w;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleBottom, surface, ref rect);
                rect.x -= shadowSide;
                rect.w = shadowSide;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleBottomLeft, surface, ref rect);
                rect.y = position.y;
                rect.h = position.h;
                SDL.SDL_BlitScaled(RenderingUtils.CircleSurface, ref RenderingUtils.CircleLeft, surface, ref rect);
            }
        }

        public void Repaint()
        {
            if (!Ui.IsMainThread())
                throw new NotSupportedException("This should be called from the main thread");
            repaintRequired = true;
        }

        protected internal virtual void Close()
        {
            visible = false;
            parent = null;
            SDL.SDL_DestroyWindow(window);
            window = renderer = surface = IntPtr.Zero;
        }

        private void Focus()
        {
            if (window != IntPtr.Zero)
            {
                SDL.SDL_RaiseWindow(window);
                SDL.SDL_RestoreWindow(window);
                SDL.SDL_SetWindowInputFocus(window);
            }
        }

        // TODO this is work-around for inability to create utility or modal window in SDL2
        // Fake utility windows are closed on focus lost
        public void FocusLost()
        {
            if (parent != null)
            {
                Close();
            }
        }

        public void SetNextRepaint(long nextRepaintTime)
        {
            if (this.nextRepaintTime > nextRepaintTime)
                this.nextRepaintTime = nextRepaintTime;
        }
    }
}