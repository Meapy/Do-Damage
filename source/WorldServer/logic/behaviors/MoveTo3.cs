using System;
using Shared.resources;
using WorldServer.core.objects;
using WorldServer.core.structures;
using WorldServer.core.worlds;
using WorldServer.utils;

namespace WorldServer.logic.behaviors
{
    internal class MoveTo3 : CycleBehavior
    {
        private readonly float baseX;
        private readonly float baseY;
        private readonly bool instant;
        private readonly bool isMapPosition;
        private readonly float speed;
        private readonly int warnMs;
        private readonly uint warnColor;

        private bool once;
        private bool returned;
        private float X;
        private float Y;

        public MoveTo3(float X, float Y, double speed = 2, bool once = false, bool isMapPosition = false, bool instant = false,
            int warnMs = 0, uint warnColor = 0xFFFFFFFF)
        {
            this.isMapPosition = isMapPosition;
            this.speed = (float)speed;
            this.once = once;
            this.instant = instant;
            this.warnMs = warnMs;
            this.warnColor = warnColor;

            baseX = X;
            baseY = Y;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state)
        {
            if (!isMapPosition)
            {
                X = baseX + host.X;
                Y = baseY + host.Y;
            }
            else
            {
                X = baseX;
                Y = baseY;
            }

            if (!instant)
                return;
            // a jump with a warning: where it will appear is marked on the ground first
            // (Strike.Arrival), and it goes there once the mark has filled
            if (warnMs > 0)
            {
                Strike.Arrival(host, new Position(X, Y), warnMs, warnColor);
                state = warnMs;
                return;
            }
            host.Move(X, Y);
        }

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (instant)
            {
                if (state is int left)
                {
                    left -= time.ElapsedMsDelta;
                    if (left > 0)
                        state = left;
                    else
                    {
                        state = null;
                        host.Move(X, Y);
                    }
                }
                return;
            }

            if (!returned)
            {
                if (host.HasConditionEffect(ConditionEffectIndex.Paralyzed))
                    return;

                var spd = host.GetSpeed(speed) * time.BehaviourTickTime;

                if (Math.Abs(X - host.X) > 0.5 || Math.Abs(Y - host.Y) > 0.5)
                {
                    var vect = new Vector2(X, Y) - new Vector2(host.X, host.Y);
                    vect.Normalize();
                    vect *= spd;

                    host.Move(host.X + vect.X, host.Y + vect.Y);

                    if (host.X == X && host.Y == Y && once)
                    {
                        once = true;
                        returned = true;
                    }
                }
            }
        }
    }
}
