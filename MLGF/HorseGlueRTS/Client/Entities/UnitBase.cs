﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SFML.Graphics;
using SFML.Window;
using Shared;
using System.Diagnostics;


namespace Client.Entities
{
    class UnitBase : EntityBase
    {

        
        public enum UnitState : byte
        {
            Agro,
            Standard,
        }

        public enum AnimationTypes: byte
        {
            Idle,
            Moving,
            Attacking,
            SpellCast,
            IdleWithResources,
            MovingWithResources,
        }

        public float Speed;
        public UnitState State;
        public float Range;
        public ushort AttackDelay;

        private Stopwatch attackTimer;

        protected EntityBase EntityToAttack { get; private set; }

        private bool allowMovement;


        protected Dictionary<AnimationTypes, AnimatedSprite> Sprites;
        protected AnimationTypes CurrentAnimation;

        private bool _callMoveCalculations;
        private float _moveX;
        private float _moveY;
        private float _moveAngle;

        private bool _moveXCompleted, _moveYCompleted;



        public UnitBase()
        {
            _moveXCompleted = false;
            _moveYCompleted = false;

            EntityToAttack = null;
            allowMovement = false;

            Speed = 0;
            State = UnitState.Agro;
            Range = 1000;
            AttackDelay = 2000;
            attackTimer = new Stopwatch();
            attackTimer.Restart();

            CurrentAnimation = AnimationTypes.Idle;
            Sprites = new Dictionary<AnimationTypes, AnimatedSprite>();

            _moveAngle = 0;
            _moveX = 0;
            _moveY = 0;
            _callMoveCalculations = true;

            const byte AnimationTypeCount = 6;
            for (int i = 0; i < AnimationTypeCount; i++)
            {
                Sprites.Add((AnimationTypes) i, new AnimatedSprite(100));
            }

        }

        protected override void ParseCustom(MemoryStream memoryStream)
        {
            var reader = new BinaryReader(memoryStream);
            var signature = (UnitSignature) reader.ReadByte();

            switch (signature)
            {
                case UnitSignature.RallyCompleted:
                    {
                        var posX = reader.ReadSingle();
                        var posY = reader.ReadSingle();

                        Position = new Vector2f(posX, posY);
                        rallyPoints.Clear();

                        _callMoveCalculations = true;
                    }
                    break;
                case UnitSignature.Attack:
                    {
                        ushort entityWorldId = reader.ReadUInt16();
                        if(WorldEntities.ContainsKey(entityWorldId))
                            onAttack(WorldEntities[entityWorldId]);
                    }
                    break;
                case UnitSignature.PopFirstRally:
                    {
                        var posX = reader.ReadSingle();
                        var posY = reader.ReadSingle();

                        Position = new Vector2f(posX, posY);
                        if (rallyPoints.Count > 0)
                            rallyPoints.RemoveAt(0);
                        _callMoveCalculations = true;
                    }
                    break;
                case UnitSignature.ClearRally:
                    rallyPoints.Clear();
                    _callMoveCalculations = true;
                    break;
                case UnitSignature.ChangeMovementAllow:
                    allowMovement = reader.ReadBoolean();
                    break;
                default:
                    break;
            }
        }


        protected virtual void onAttack(EntityBase ent)
        {
            ent.OnTakeDamage(10, Entity.DamageElement.Normal);
        }


        protected override void ParseUpdate(MemoryStream memoryStream)
        {
            var reader = new BinaryReader(memoryStream);

            var hasEntToUse = reader.ReadBoolean();
            if(hasEntToUse)
            {
                var id = reader.ReadUInt16();
                if(WorldEntities.ContainsKey(id))
                {
                    EntityToUse = WorldEntities[id];
                }
            }

            Health = reader.ReadSingle();
            State = (UnitState) reader.ReadByte();
            Position = new Vector2f(reader.ReadSingle(), reader.ReadSingle());
            Speed = reader.ReadSingle();
            Energy = reader.ReadUInt16();
            Range = reader.ReadSingle();
            allowMovement = reader.ReadBoolean();

            var rallyCount = reader.ReadByte();
            rallyPoints.Clear();
            for(var i = 0; i < rallyCount; i++)
            {
                rallyPoints.Add(new Vector2f(reader.ReadSingle(), reader.ReadSingle()));
            }
        }

        protected virtual void onSetIdleAnimation()
        {
            CurrentAnimation = AnimationTypes.Idle;
        }

        protected virtual void onSetMovingAnimation()
        {
            CurrentAnimation = AnimationTypes.Moving;
        }

        public override void OnMove()
        {
            base.OnMove();
            _callMoveCalculations = true;
            _moveXCompleted = false;
            _moveYCompleted = false;
        }

        public override void Update(float ms)
        {
            if (Sprites.ContainsKey(CurrentAnimation))
            {
                Sprites[CurrentAnimation].Update(ms);
            }

            if(CurrentAnimation != AnimationTypes.SpellCast && (rallyPoints.Count == 0 || allowMovement == false)) onSetIdleAnimation();

            if (!allowMovement || rallyPoints.Count == 0) return;
            if(CurrentAnimation != AnimationTypes.SpellCast)
                onSetMovingAnimation();

            Vector2f destination = rallyPoints[0];

            /*
            if (_callMoveCalculations)
            {
                debugStopwatch.Restart();
                _callMoveCalculations = false;

                _moveAngle = (float)Math.Atan2(destination.Y - Position.Y, destination.X - Position.X);
                _moveX = (float)Math.Cos(_moveAngle);
                _moveY = (float)Math.Sin(_moveAngle);
            }

            Position.X += (_moveX * Speed) * ms;
            Position.Y += (_moveY * Speed) * ms;

            bool completedDestinationX = (_moveX > 0 && (int)Position.X >= (int)destination.X) || _moveX == 0 ||
                                         (_moveX < 0 && (int)Position.X <= (int)destination.X) || (int)Position.X == (int)destination.X;

            bool completedDestinationY = (_moveY > 0 && (int)Position.Y >= (int)destination.Y) || _moveY == 0 || 
                                         (_moveY < 0 && (int)Position.Y <= (int)destination.Y) || (int)Position.Y == (int)destination.Y;

            if (completedDestinationX && completedDestinationY)*/
            if ((int)Position.X < (int)destination.X)
            {
                Position.X += Speed * ms;
                if ((int)Position.X >= (int)destination.X) _moveXCompleted = true;
            }
            if ((int)Position.Y < (int)destination.Y)
            {
                Position.Y += Speed * ms;
                if ((int)Position.Y >= (int)destination.Y) _moveYCompleted = true;
            }
            if ((int)Position.X > destination.X)
            {
                Position.X -= Speed * ms;
                if ((int)Position.X <= (int)destination.X) _moveXCompleted = true;
            }
            if ((int)Position.Y > (int)destination.Y)
            {
                Position.Y -= Speed * ms;
                if ((int)Position.Y <= (int)destination.Y) _moveYCompleted = true;
            }

            if ((int)Position.X == (int)destination.X) _moveXCompleted = true;
            if ((int)Position.Y == (int)destination.Y) _moveYCompleted = true;


            if (_moveXCompleted && _moveYCompleted)
            {
                _moveXCompleted = false;
                _moveYCompleted = false;

                if(rallyPoints.Count == 1)
                    Position = destination;
                _callMoveCalculations = true;
                rallyPoints.RemoveAt(0);
            }
        }

        public override void Render(RenderTarget target)
        {
            if(Sprites.ContainsKey(CurrentAnimation) && Sprites[CurrentAnimation].Sprites.Count > 0)
            {
                Sprite spr = Sprites[CurrentAnimation].CurrentSprite;
                
                spr.Position = Position;
                spr.Origin = new Vector2f(spr.TextureRect.Width/2, spr.TextureRect.Height/2);
                target.Draw(spr);
            }
        }

        protected void debugDrawRange(RenderTarget target)
        {
            CircleShape circle = new CircleShape(Range);
            circle.Origin = new Vector2f(circle.Radius, circle.Radius);
            circle.Position = Position;
            circle.FillColor = new Color(255, 0, 0, 100);

            target.Draw(circle);
        }
    }
}
