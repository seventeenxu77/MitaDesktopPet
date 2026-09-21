using System;
using System.Collections;
using UnityEngine;

namespace DesktopPet
{
    public sealed class MockChatBackend : MonoBehaviour, IChatBackend
    {
        [SerializeField, Min(0f)] private float responseDelay = 0.35f;

        public bool IsBusy { get; private set; }
        private Action<string> _error;

        public void Send(string userText, Action<string> onCompleted, Action<string> onError)
        {
            if (IsBusy)
            {
                onError?.Invoke("我还在想上一句话。");
                return;
            }

            _error = onError;
            StartCoroutine(Reply(userText, onCompleted));
        }

        private IEnumerator Reply(string userText, Action<string> onCompleted)
        {
            IsBusy = true;
            if (responseDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(responseDelay);
            }

            IsBusy = false;
            _error = null;
            onCompleted?.Invoke("我听到了：" + userText + "\n（当前是本地假回复，下一阶段接入 OpenAI。）");
        }

        public void Cancel()
        {
            StopAllCoroutines();
            IsBusy = false;
            var callback = _error; _error = null;
            callback?.Invoke("已取消本地测试回复。");
        }
        public void ResetConversation() { Cancel(); }
        private void OnDisable() { Cancel(); }
    }
}
