using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopPet
{
    // Expressions, chest and shoulders are independent of the base Animator. Head and
    // eyelids are composed by their existing owners, avoiding competing LateUpdates.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class PetClickReactionController : MonoBehaviour
    {
        private enum Reaction { None, Pet, Poke, Annoyed }
        private static readonly string[] Shapes = { "SmileSmall", "Smile :>", "AnnoyMiddle", "AnnoyBig",
            "EyeBrowsUp", "EyeBrowsDown", "EyesWonder", "EyesAngry" };
        // Readable at desktop scale, based on the shy/smile/surprise/discontent
        // expressions. Do not sample their entire face tracks over the sleepy eyelids.
        private static readonly float[] PetFace = {52,24,0,0,16,0,0,0};
        private static readonly float[] PokeFace = {0,0,0,20,55,0,35,0};
        private static readonly float[] AnnoyedFace = {0,0,62,0,0,45,0,40};
        private static readonly int DraggingHash = Animator.StringToHash("IsDragging");
        private DesktopWindowController _window;
        private Camera _camera;
        private Animator _animator;
        private SkinnedMeshRenderer _face;
        private Transform _head, _chest, _leftShoulder, _rightShoulder;
        private Collider[] _colliders;
        private Vector3 _headCentreLocal;
        private float _headRadiusLocal;
        private bool _hasDragParameter, _applied;
        private readonly int[] _indices = new int[Shapes.Length];
        private readonly float[] _baseFace = new float[Shapes.Length];
        private Quaternion _leftBase, _rightBase, _chestBase;
        private Reaction _reaction;
        private float _age, _duration, _lastClick = float.NegativeInfinity, _burstStart;
        private int _clickCount;
        private Vector3 _mix, _mixVelocity, _headOffset, _headVelocity;
        private Vector3 _chestOffset, _chestVelocity;
        private float _shrug, _shrugVelocity;

        internal Vector3 HeadOffset => isActiveAndEnabled ? _headOffset : Vector3.zero;
        internal float MouseLookInfluence => isActiveAndEnabled ? 1-.75f*Mathf.Clamp01(_mix.x+_mix.y+_mix.z) : 1;
        internal bool IsReacting => isActiveAndEnabled && (_age < _duration || _mix.sqrMagnitude > .000025f);
        internal float BlendEyeOpening(float sleepyOpening)
        {
            if(!isActiveAndEnabled) return sleepyOpening;
            return Mathf.Clamp01(sleepyOpening*(1-_mix.x-_mix.y-_mix.z)+.72f*_mix.y+.50f*_mix.z);
        }

        private void OnEnable()
        {
            RestoreOverlay();
            ResolveReferences();
            _reaction=Reaction.None; _age=_duration=0;
            _mix=_mixVelocity=_headOffset=_headVelocity=Vector3.zero;
            _chestOffset=_chestVelocity=Vector3.zero;
            _shrug=_shrugVelocity=0;
            _clickCount=0; _lastClick=float.NegativeInfinity;
        }
        private void ResolveReferences()
        {
            _window=FindObjectOfType<DesktopWindowController>();
            _camera=Camera.main;
            _animator=GetComponent<Animator>();
            _colliders=GetComponentsInChildren<Collider>();
            _head=transform.Find("Armature/Hips/Spine/Chest/Neck2/Neck1/Head");
            _chest=transform.Find("Armature/Hips/Spine/Chest");
            foreach(var bone in GetComponentsInChildren<Transform>(true))
            {
                if(bone.name=="Left shoulder") _leftShoulder=bone;
                if(bone.name=="Right shoulder") _rightShoulder=bone;
            }
            foreach(var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(skin.sharedMesh!=null && skin.sharedMesh.GetBlendShapeIndex("Blink")>=0)
                { _face=skin; break; }
            for(int i=0;i<Shapes.Length;i++)
                _indices[i]=_face!=null ? _face.sharedMesh.GetBlendShapeIndex(Shapes[i]) : -1;
            if(_head!=null && _face!=null)
            {
                // Cache in head space so the hit zone follows nodding and sitting.
                _headCentreLocal=_head.InverseTransformPoint(_face.bounds.center);
                float radius=Mathf.Max(_face.bounds.extents.x,_face.bounds.extents.y*.7f);
                _headRadiusLocal=_head.InverseTransformVector(Vector3.right*radius).magnitude;
            }
            _hasDragParameter=false;
            if(_animator!=null && _animator.runtimeAnimatorController!=null)
                foreach(var parameter in _animator.parameters)
                    if(parameter.nameHash==DraggingHash && parameter.type==AnimatorControllerParameterType.Bool)
                        _hasDragParameter=true;
        }
        private bool IsDragging => (_window!=null && _window.IsDragging) ||
            (_hasDragParameter && _animator!=null && _animator.GetBool(DraggingHash));

        private void Update()
        {
            RestoreOverlay();
            if(Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !IsDragging) TryClick();
            AdvanceReaction(Time.unscaledDeltaTime,IsDragging);
        }
        private void LateUpdate() { ApplyOverlay(); }
        private void TryClick()
        {
            if(_window==null || _camera==null) ResolveReferences();
            if(_window==null || _camera==null || !_window.TryGetCursorClientPosition(out var pointer)) return;
            if(DesktopChatController.Active!=null && DesktopChatController.Active.IsPointerOverChat(pointer)) return;
            if(EventSystem.current!=null && EventSystem.current.IsPointerOverGameObject()) return;
            if(TryPick(_camera,pointer,out var head)) RegisterClick(head,Time.unscaledTime);
        }
        internal bool TryPick(Camera camera,Vector2 pointer,out bool head)
        {
            head=false;
            if(camera==null || !camera.pixelRect.Contains(pointer) || _colliders==null) return false;
            var ray=camera.ScreenPointToRay(pointer);
            foreach(var collider in _colliders)
                if(collider!=null && collider.enabled && collider.gameObject.activeInHierarchy && collider.Raycast(ray,out var hit,1000f))
                { head=IsHeadPoint(camera,pointer); return true; }
            return false;
        }
        internal bool IsHeadPoint(Camera camera,Vector2 pointer)
        {
            if(_head==null || _headRadiusLocal<=0 || camera==null) return false;
            var centre=_head.TransformPoint(_headCentreLocal);
            float radius=_head.TransformVector(Vector3.right*_headRadiusLocal).magnitude;
            var screen=camera.WorldToScreenPoint(centre);
            if(screen.z<=0) return false;
            float pixels=Vector2.Distance(screen,camera.WorldToScreenPoint(centre+camera.transform.up*radius));
            if(pixels<1) return false;
            // Forehead/crown/hair, not the neck. Face below this zone counts as a poke.
            var offset=(pointer-(Vector2)screen)/pixels;
            return offset.y>=0 && offset.y<=1.6f && Mathf.Abs(offset.x)<=1.2f &&
                offset.x*offset.x+(offset.y-.55f)*(offset.y-.55f)<=1.45f;
        }
        internal bool RegisterClick(bool head,float now)
        {
            if(!isActiveAndEnabled || IsDragging || now-_lastClick<.15f) return false;
            if(_clickCount==0 || now-_burstStart>1.8f) { _clickCount=0; _burstStart=now; }
            _clickCount++; _lastClick=now;
            _reaction=_clickCount>=3 ? Reaction.Annoyed : head ? Reaction.Pet : Reaction.Poke;
            _age=0;
            _duration=_reaction==Reaction.Pet ? 2.8f : _reaction==Reaction.Poke ? 1.9f : 3f;
            return true;
        }
        internal void AdvanceReaction(float dt,bool dragging)
        {
            dt=Mathf.Max(0,dt);
            _age+=dt;
            if(dragging) { _age=_duration; _clickCount=0; }
            var target=Vector3.zero;
            if(_age<_duration)
            {
                float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(_duration-.85f,_duration,_age));
                if(_reaction==Reaction.Pet) target.x=fade;
                else if(_reaction==Reaction.Poke) target.y=fade;
                else if(_reaction==Reaction.Annoyed) target.z=fade;
            }
            if(dt>0)
            {
                _mix=Vector3.SmoothDamp(_mix,target,ref _mixVelocity,dragging ? .09f : .10f,Mathf.Infinity,dt);
                _mix=Vector3.Max(Vector3.zero,_mix);
                float sum=_mix.x+_mix.y+_mix.z;
                if(sum>1) _mix/=sum;
                float shake=Mathf.Sin(_age*7)*17*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.4f,2.2f,_age)));
                var headTarget=new Vector3(9,0,17)*_mix.x+new Vector3(-16,5,-7)*_mix.y+
                    new Vector3(4,shake,-3)*_mix.z;
                _headOffset=Vector3.SmoothDamp(_headOffset,headTarget,ref _headVelocity,.12f,Mathf.Infinity,dt);
                var chestTarget=new Vector3(3,0,4)*_mix.x+new Vector3(-5,0,-5)*_mix.y+new Vector3(2,0,3)*_mix.z;
                _chestOffset=Vector3.SmoothDamp(_chestOffset,chestTarget,ref _chestVelocity,.15f,Mathf.Infinity,dt);
                _shrug=Mathf.SmoothDamp(_shrug,9f*_mix.y+4f*_mix.z,ref _shrugVelocity,.12f,Mathf.Infinity,dt);
            }
        }
        internal void ApplyOverlay()
        {
            RestoreOverlay();
            // Chest-only lean makes the silhouette readable without moving the
            // pelvis/legs. Restore it before the gaze owner restores head/neck locals.
            if(_chest!=null)
            {
                _chestBase=_chest.localRotation;
                _chest.rotation=Quaternion.AngleAxis(_chestOffset.z,transform.forward)*
                    Quaternion.AngleAxis(_chestOffset.x,transform.right)*_chest.rotation;
            }
            float sum=_mix.x+_mix.y+_mix.z;
            if(_face!=null)
                for(int i=0;i<Shapes.Length;i++) if(_indices[i]>=0)
                {
                    _baseFace[i]=_face.GetBlendShapeWeight(_indices[i]);
                    float target=_baseFace[i]*(1-sum)+PetFace[i]*_mix.x+PokeFace[i]*_mix.y+AnnoyedFace[i]*_mix.z;
                    _face.SetBlendShapeWeight(_indices[i],target);
                }
            if(_leftShoulder!=null)
            {
                _leftBase=_leftShoulder.localRotation;
                _leftShoulder.rotation=Quaternion.AngleAxis(-_shrug,Vector3.forward)*_leftShoulder.rotation;
            }
            if(_rightShoulder!=null)
            {
                _rightBase=_rightShoulder.localRotation;
                _rightShoulder.rotation=Quaternion.AngleAxis(_shrug,Vector3.forward)*_rightShoulder.rotation;
            }
            _applied=true;
        }
        internal void RestoreOverlay()
        {
            if(!_applied) return;
            if(_chest!=null) _chest.localRotation=_chestBase;
            if(_face!=null)
                for(int i=0;i<Shapes.Length;i++) if(_indices[i]>=0) _face.SetBlendShapeWeight(_indices[i],_baseFace[i]);
            if(_leftShoulder!=null) _leftShoulder.localRotation=_leftBase;
            if(_rightShoulder!=null) _rightShoulder.localRotation=_rightBase;
            _applied=false;
        }
        private void OnDisable()
        {
            RestoreOverlay();
            _mix=_mixVelocity=_headOffset=_headVelocity=Vector3.zero;
            _chestOffset=_chestVelocity=Vector3.zero;
            _age=_duration=0;
        }
    }
}
