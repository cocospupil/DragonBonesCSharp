﻿using System;
using System.Collections.Generic;

namespace DragonBones
{
    /**
     * @private
     */
    internal enum BoneTransformDirty
    {
        None = 0,
        Self = 1,
        All = 2
    }

    /**
     * @language zh_CN
     * 骨骼，一个骨架中可以包含多个骨骼，骨骼以树状结构组成骨架。
     * 骨骼在骨骼动画体系中是最重要的逻辑单元之一，负责动画中的平移旋转缩放的实现。
     * @see dragonBones.BoneData
     * @see dragonBones.Armature
     * @see dragonBones.Slot
     * @version DragonBones 3.0
     */
    public class Bone : TransformObject
    {
        /**
         * @language zh_CN
         * 是否继承父骨骼的平移。 [true: 继承, false: 不继承]
         * @version DragonBones 3.0
         */
        public bool inheritTranslation;

        /**
         * @language zh_CN
         * 是否继承父骨骼的旋转。 [true: 继承, false: 不继承]
         * @version DragonBones 3.0
         */
        public bool inheritRotation;

        /**
         * @language zh_CN
         * 是否继承父骨骼的缩放。 [true: 继承, false: 不继承]
         * @version DragonBones 4.5
         */
        public bool inheritScale;

        /**
         * @private
         */
        public bool ikBendPositive;

        /**
         * @language zh_CN
         * 骨骼长度。
         * @version DragonBones 4.5
         */
        public float length;

        /**
         * @private
         */
        public float ikWeight;

        /**
         * @private
         */
        internal BoneTransformDirty _transformDirty;
        
        private bool _visible;
        private int _cachedFrameIndex;
        private uint _ikChain;
        private int _ikChainIndex;
        /**
         * @private
         */
        internal int _updateState;
        /**
         * @private
         */
        internal int _blendLayer;
        /**
         * @private
         */
        internal float _blendLeftWeight;
        /**
         * @private
         */
        internal float _blendTotalWeight;
        /**
         * @private
         */
        internal readonly Transform _animationPose = new Transform();
        private readonly List<Bone> _bones = new List<Bone>();
        private readonly List<Slot> _slots = new List<Slot>();
        private BoneData _boneData;
        private Bone _ik;
        /**
         * @private
         */
        internal List<int> _cachedFrameIndices;

        /**
         * @private
         */
        public Bone()
        {
        }

        /**
         * @private
         */
        protected override void _onClear()
        {
            base._onClear();

            inheritTranslation = false;
            inheritRotation = false;
            inheritScale = false;
            ikBendPositive = false;
            length = 0.0f;
            ikWeight = 0.0f;

            _transformDirty = BoneTransformDirty.None;
            _visible = true;
            _cachedFrameIndex = -1;
            _ikChain = 0;
            _ikChainIndex = 0;
            _updateState = -1;
            _blendLayer = 0;
            _blendLeftWeight = 0.0f;
            _blendTotalWeight = 0.0f;
            _animationPose.Identity();
            _bones.Clear();
            _slots.Clear();
            _boneData = null;
            _ik = null;
            _cachedFrameIndices = null;
        }

        /**
         * @private
         */
        private void _updateGlobalTransformMatrix()
        {
            this.global.x = this.origin.x + this.offset.x + _animationPose.x;
            this.global.y = this.origin.y + this.offset.y + _animationPose.y;
            this.global.skewX = this.origin.skewX + this.offset.skewX + _animationPose.skewX;
            this.global.skewY = this.origin.skewY + this.offset.skewY + _animationPose.skewY;
            this.global.scaleX = this.origin.scaleX * this.offset.scaleX * _animationPose.scaleX;
            this.global.scaleY = this.origin.scaleY * this.offset.scaleY * _animationPose.scaleY;

            if (this._parent != null)
            {
                var parentRotation = this._parent.global.skewY; // Only inherit skew y.
                var parentMatrix = this._parent.globalTransformMatrix;

                if (inheritScale)
                {
                    if (!inheritRotation)
                    {
                        this.global.skewX -= parentRotation;
                        this.global.skewY -= parentRotation;
                    }

                    this.global.ToMatrix(this.globalTransformMatrix);
                    this.globalTransformMatrix.Concat(parentMatrix);

                    if (!this.inheritTranslation)
                    {
                        this.globalTransformMatrix.tx = this.global.x;
                        this.globalTransformMatrix.ty = this.global.y;
                    }

                    this.global.FromMatrix(this.globalTransformMatrix);
                }
                else
                {
                    if (inheritTranslation)
                    {
                        var x = this.global.x;
                        var y = this.global.y;
                        this.global.x = parentMatrix.a * x + parentMatrix.c * y + parentMatrix.tx;
                        this.global.y = parentMatrix.d * y + parentMatrix.b * x + parentMatrix.ty;
                    }

                    if (inheritRotation)
                    {
                        this.global.skewX += parentRotation;
                        this.global.skewY += parentRotation;
                    }

                    this.global.ToMatrix(this.globalTransformMatrix);
                }
            }
            else
            {
                this.global.ToMatrix(this.globalTransformMatrix);
            }

            if (_ik != null && _ikChainIndex == _ikChain && ikWeight > 0.0f)
            {
                if (inheritTranslation && _ikChain > 0 && _parent != null)
                {
                    _computeIKB();
                }
                else
                {
                    _computeIKA();
                }
            }
        }

        /**
         * @private
         */
        private void _computeIKA()
        {
            var ikGlobal = _ik.global;
            var x = this.globalTransformMatrix.a * length;
            var y = this.globalTransformMatrix.b * length;

            var ikRadian =
                (float)(
                    Math.Atan2(ikGlobal.y - this.global.y, ikGlobal.x - this.global.x) +
                    this.offset.skewY -
                    this.global.skewY * 2.0f +
                    Math.Atan2(y, x)
                ) * ikWeight; // Support offset.

            this.global.skewX += ikRadian;
            this.global.skewY += ikRadian;
            this.global.ToMatrix(this.globalTransformMatrix);
        }

        /**
         * @private
         */
        private void _computeIKB()
        {
            var parentGlobal = this._parent.global;
            var ikGlobal = _ik.global;

            var x = this.globalTransformMatrix.a * length;
            var y = this.globalTransformMatrix.b * length;

            var lLL = x * x + y * y;
            var lL = (float)Math.Sqrt(lLL);

            var dX = this.global.x - parentGlobal.x;
            var dY = this.global.y - parentGlobal.y;
            var lPP = dX * dX + dY * dY;
            var lP = (float)Math.Sqrt(lPP);

            dX = ikGlobal.x - parentGlobal.x;
            dY = ikGlobal.y - parentGlobal.y;
            var lTT = dX * dX + dY * dY;
            var lT = (float)Math.Sqrt(lTT);

            var ikRadianA = 0.0f;
            if (lL + lP <= lT || lT + lL <= lP || lT + lP <= lL)
            {
                ikRadianA = (float)Math.Atan2(ikGlobal.y - parentGlobal.y, ikGlobal.x - parentGlobal.x) + this._parent.offset.skewY; // Support offset.
                if (lL + lP <= lT)
                {
                }
                else if (lP < lL)
                {
                    ikRadianA += DragonBones.PI;
                }
            }
            else
            {
                var h = (lPP - lLL + lTT) / (2.0f * lTT);
                var r = (float)Math.Sqrt(lPP - h * h * lTT) / lT;
                var hX = parentGlobal.x + (dX * h);
                var hY = parentGlobal.y + (dY * h);
                var rX = -dY * r;
                var rY = dX * r;

                if (ikBendPositive)
                {
                    this.global.x = hX - rX;
                    this.global.y = hY - rY;
                }
                else
                {
                    this.global.x = hX + rX;
                    this.global.y = hY + rY;
                }

                ikRadianA = (float)Math.Atan2(this.global.y - parentGlobal.y, this.global.x - parentGlobal.x) + this._parent.offset.skewY; // Support offset.
            }

            ikRadianA = (ikRadianA - parentGlobal.skewY) * ikWeight;

            parentGlobal.skewX += ikRadianA;
            parentGlobal.skewY += ikRadianA;

            parentGlobal.ToMatrix(this._parent.globalTransformMatrix);
            this._parent._transformDirty = BoneTransformDirty.Self;

            this.global.x = parentGlobal.x + (float)Math.Cos(parentGlobal.skewY) * lP;
            this.global.y = parentGlobal.y + (float)Math.Sin(parentGlobal.skewY) * lP;

            var ikRadianB =
                (float)(
                    Math.Atan2(ikGlobal.y - this.global.y, ikGlobal.x - this.global.x) + this.offset.skewY -
                    this.global.skewY * 2 + Math.Atan2(y, x)
                ) * this.ikWeight; // Support offset.

            this.global.skewX += ikRadianB;
            this.global.skewY += ikRadianB;

            this.global.ToMatrix(this.globalTransformMatrix);
        }

        /**
         * @private
         */
        internal void _init(BoneData boneData)
        {
            if (_boneData != null) {
                return;
            }

            _boneData = boneData;

            inheritTranslation = _boneData.inheritTranslation;
            inheritRotation = _boneData.inheritRotation;
            inheritScale = _boneData.inheritScale;
            length = _boneData.length;
            this.name = _boneData.name;
            this.origin =_boneData.transform;
        }

        /**
         * @private
         */
        internal override void _setArmature(Armature value)
        {
            if (this._armature == value)
            {
                return;
            }

            _ik = null;

            List<Slot> oldSlots = null;
            List<Bone> oldBones = null;

            if (this._armature != null)
            {
                oldSlots = GetSlots();
                oldBones = GetBones();
                this._armature._removeBoneFromBoneList(this);
            }

            this._armature = value;

            if (this._armature != null)
            {
                this._armature._addBoneToBoneList(this);
            }

            if (oldSlots != null)
            {
                foreach (var slot in oldSlots)
                {
                    if (slot.parent == this)
                    {
                        slot._setArmature(this._armature);
                    }
                }
            }

            if (oldBones != null)
            {
                foreach (var bone in oldBones)
                {
                    if (bone.parent == this)
                    {
                        bone._setArmature(this._armature);
                    }
                }
            }
        }

        /**
         * @private
         */
        internal void _setIK(Bone value, uint chain, int chainIndex)
        {
            if (value != null)
            {
                if (chain == chainIndex)
                {
                    var chainEnd = this._parent;
                    if (chain > 0 && chainEnd != null)
                    {
                        chain = 1;
                    }
                    else
                    {
                        chain = 0;
                        chainIndex = 0;
                        chainEnd = this;
                    }

                    if (chainEnd == value || chainEnd.Contains(value))
                    {
                        value = null;
                        chain = 0;
                        chainIndex = 0;
                    }
                    else
                    {
                        var ancestor = value;
                        while (ancestor.ik != null && ancestor.ikChain > 0)
                        {
                            if (chainEnd.Contains(ancestor.ik))
                            {
                                value = null;
                                chain = 0;
                                chainIndex = 0;
                                break;
                            }

                            ancestor = ancestor.parent;
                        }
                    }
                }
            }
            else
            {
                chain = 0;
                chainIndex = 0;
            }

            _ik = value;
            _ikChain = chain;
            _ikChainIndex = chainIndex;

            if (this._armature != null)
            {
                this._armature._bonesDirty = true;
            }
        }

        /**
         * @private
         */
        internal void _update(int cacheFrameIndex)
        {
            _updateState = -1;

            if (cacheFrameIndex >= 0 && _cachedFrameIndices != null)
            {
                var cachedFrameIndex = _cachedFrameIndices[cacheFrameIndex];
                if (cachedFrameIndex >= 0 && _cachedFrameIndex == cachedFrameIndex) // Same cache.
                {
                    _transformDirty = BoneTransformDirty.None;
                }
                else if (cachedFrameIndex >= 0.0f) // Has been Cached.
                {
                    _transformDirty = BoneTransformDirty.All;
                    _cachedFrameIndex = cachedFrameIndex;
                }
                else if (
                    _transformDirty == BoneTransformDirty.All ||
                    (_parent != null && _parent._transformDirty != BoneTransformDirty.None) ||
                    (_ik != null && ikWeight > 0.0f && _ik._transformDirty != BoneTransformDirty.None)
                ) // Dirty.
                {
                    _transformDirty = BoneTransformDirty.All;
                    _cachedFrameIndex = -1;
                }
                else if (_cachedFrameIndex >= 0) // Same cache but not cached yet.
                {
                    _transformDirty = BoneTransformDirty.None;
                    _cachedFrameIndices[cacheFrameIndex] = _cachedFrameIndex;
                }
                else // Dirty.
                {
                    _transformDirty = BoneTransformDirty.All;
                    _cachedFrameIndex = -1;
                }
            }
            else if (
                _transformDirty == BoneTransformDirty.All ||
                (_parent != null && _parent._transformDirty != BoneTransformDirty.None) ||
                (_ik != null && ikWeight > 0.0f && _ik._transformDirty != BoneTransformDirty.None)
            )
            {
                cacheFrameIndex = -1;
                _transformDirty = BoneTransformDirty.All; // For update children and ik children.
                _cachedFrameIndex = -1;
            }

            if (_transformDirty != BoneTransformDirty.None)
            {
                if (_transformDirty == BoneTransformDirty.All)
                {
                    _transformDirty = BoneTransformDirty.Self;

                    if (_cachedFrameIndex < 0)
                    {
                        _updateGlobalTransformMatrix();

                        if (cacheFrameIndex >= 0)
                        {
                            _cachedFrameIndex = _cachedFrameIndices[cacheFrameIndex] = _armature._armatureData.SetCacheFrame(this.globalTransformMatrix, this.global);
                        }
                    }
                    else
                    {
                        _armature._armatureData.GetCacheFrame(this.globalTransformMatrix, this.global, _cachedFrameIndex);
                    }

                    _updateState = 0;
                }
                else
                {
                    _transformDirty = BoneTransformDirty.None;
                }
            }
        }

        /**
         * @language zh_CN
         * 下一帧更新变换。 (当骨骼没有动画状态或动画状态播放完成时，骨骼将不在更新)
         * @version DragonBones 3.0
         */
        public void InvalidUpdate()
        {
            _transformDirty = BoneTransformDirty.All;
        }

        /**
         * @language zh_CN
         * 是否包含某个指定的骨骼或插槽。
         * @returns [true: 包含，false: 不包含]
         * @see dragonBones.TransformObject
         * @version DragonBones 3.0
         */
        public bool Contains(TransformObject child)
        {
            if (child != null)
            {
                if (child == this)
                {
                    return false;
                }

                var ancestor = child;
                while (ancestor != this && ancestor != null)
                {
                    ancestor = ancestor.parent;
                }

                return ancestor == this;
            }

            return false;
        }

        /**
         * @language zh_CN
         * 所有的子骨骼。
         * @version DragonBones 3.0
         */
        public List<Bone> GetBones()
        {
            _bones.Clear();

            var bones = this._armature.GetBones();
            foreach (var bone in bones)
            {
                if (bone.parent == this)
                {
                    _bones.Add(bone);
                }
            }

            return _bones;
        }

        /**
         * @language zh_CN
         * 所有的插槽。
         * @see dragonBones.Slot
         * @version DragonBones 3.0
         */
        public List<Slot> GetSlots()
        {
            _slots.Clear();

            var slots = this._armature.GetSlots();
            foreach (var slot in slots)
            {
                if (slot.parent == this)
                {
                    _slots.Add(slot);
                }
            }

            return _slots;
        }

        /**
         * @language zh_CN
         * 控制此骨骼所有插槽的显示。
         * @default true
         * @see dragonBones.Slot
         * @version DragonBones 3.0
         */
        public bool visible
        {
            get { return _visible; }

            set
            {
                if (_visible == value)
                {
                    return;
                }

                _visible = value;
                var slots = this._armature.GetSlots();
                foreach (var slot in slots)
                {
                    if (slot._parent == this)
                    {
                        slot._updateVisible();
                    }
                }
            }
        }

        /**
         * @deprecated
         */
        public uint ikChain
        {
            get { return _ikChain; }
        }
        /**
         * @deprecated
         */
        public int ikChainIndex
        {
            get { return _ikChainIndex; }
        }
        /**
         * @deprecated
         */
        public Bone ik
        {
            get { return _ik; }
        }
    }
}