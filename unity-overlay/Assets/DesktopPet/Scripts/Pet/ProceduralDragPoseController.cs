using UnityEngine;

namespace DesktopPet
{
    // Damped pendulum about the authored hip anchor, driven by screen-space
    // changes in drag velocity. No arbitrary idle sine and no accumulated pose.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class ProceduralDragPoseController : MonoBehaviour
    {
        [SerializeField] private DesktopWindowController desktopWindow;
        [SerializeField, Range(5f,30f)] private float swingLimitDegrees=20f;
        private Camera _camera;
        private Transform _hips;
        private Quaternion _baseLocal;
        private bool _applied;
        private float _weight;
        private Vector2 _angle, _angularVelocity, _previousVelocity;

        internal float ActiveWeight => isActiveAndEnabled ? _weight : 0;
        internal Vector2 SwingAngles => _angle*_weight;

        private void OnEnable()
        {
            RestorePose();
            _hips=transform.Find("Armature/Hips");
            _camera=Camera.main;
            if(desktopWindow==null) desktopWindow=FindObjectOfType<DesktopWindowController>();
            _weight=0; _angle=_angularVelocity=_previousVelocity=Vector2.zero;
        }
        private void Update() { RestorePose(); }
        private void LateUpdate()
        {
            bool dragging=desktopWindow!=null && desktopWindow.IsDragging;
            var velocity=dragging ? desktopWindow.DragVelocityPixelsPerSecond*(700f/Mathf.Max(200,Screen.height)) : Vector2.zero;
            StepInertia(velocity,dragging,Time.unscaledDeltaTime);
            ApplyPose();
        }
        internal void StepInertia(Vector2 velocity,bool dragging,float deltaTime)
        {
            float dt=Mathf.Clamp(deltaTime,0,.1f);
            if(dt<=0) return;
            velocity=dragging ? Vector2.ClampMagnitude(velocity,2400) : Vector2.zero;
            _weight=Mathf.MoveTowards(_weight,dragging ? 1 : 0,dt/(dragging ? .25f : .22f));
            if(_weight<=0 && !dragging)
            { _angle=_angularVelocity=_previousVelocity=Vector2.zero; return; }
            int count=Mathf.Max(1,Mathf.CeilToInt(dt*120));
            float step=dt/count;
            var change=dragging ? velocity-_previousVelocity : Vector2.zero;
            var impulse=new Vector2(-change.y*.035f,-change.x*.095f)/count;
            const float frequency=7.2f, damping=2*.42f*frequency;
            for(int i=0;i<count;i++)
            {
                var current=Vector2.Lerp(_previousVelocity,velocity,(i+1f)/count);
                var target=dragging ? new Vector2(-current.y*.0015f,-current.x*.003f) : Vector2.zero;
                _angularVelocity+=impulse;
                _angularVelocity+=((target-_angle)*(frequency*frequency)-damping*_angularVelocity)*step;
                _angularVelocity=Vector2.ClampMagnitude(_angularVelocity,180);
                _angle+=_angularVelocity*step;
                Limit(ref _angle.x,ref _angularVelocity.x,9);
                Limit(ref _angle.y,ref _angularVelocity.y,swingLimitDegrees);
            }
            _previousVelocity=velocity;
        }
        private static void Limit(ref float angle,ref float speed,float limit)
        {
            if(Mathf.Abs(angle)<=limit) return;
            angle=Mathf.Clamp(angle,-limit,limit);
            if(Mathf.Sign(speed)==Mathf.Sign(angle)) speed=0;
        }
        internal void ApplyPose()
        {
            RestorePose();
            if(_hips==null || _weight<=0) return;
            _baseLocal=_hips.localRotation;
            var cameraForward=_camera!=null ? _camera.transform.forward : Vector3.back;
            var cameraRight=_camera!=null ? _camera.transform.right : Vector3.left;
            _hips.rotation=Quaternion.AngleAxis(_angle.y*_weight,cameraForward)*
                Quaternion.AngleAxis(_angle.x*_weight,cameraRight)*_hips.rotation;
            _applied=true;
        }
        internal void RestorePose()
        {
            if(_applied && _hips!=null) _hips.localRotation=_baseLocal;
            _applied=false;
        }
        private void OnDisable()
        {
            RestorePose();
            _weight=0; _angle=_angularVelocity=_previousVelocity=Vector2.zero;
        }
    }
}
