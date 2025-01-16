using Solitaire.Models;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Solitaire.Presenters
{
    public class PopupMatchPresenter : OrientationAwarePresenter
    {
        [SerializeField] private TMP_InputField _inputCardCode;

        [SerializeField] private Button _buttonRestart;

        [SerializeField] private Button _buttonNewMatch;

        [SerializeField] private Button _buttonContinue;

        [SerializeField] private RectTransform _panelRect;

        [Inject] private readonly Game _game;
        private RectTransform _rectCardCode;
        private RectTransform _rectContinue;
        private RectTransform _rectNewMatch;
        private RectTransform _rectRestart;

        private void Awake()
        {
            _rectCardCode = _inputCardCode.GetComponent<RectTransform>();
            _rectRestart = _buttonRestart.GetComponent<RectTransform>();
            _rectNewMatch = _buttonNewMatch.GetComponent<RectTransform>();
            _rectContinue = _buttonContinue.GetComponent<RectTransform>();
        }

        protected override void Start()
        {
            base.Start();

            // Bind commands
            _inputCardCode.onValueChanged.AddListener(OnCardCodeChange);
            _game.RestartCommand.BindTo(_buttonRestart).AddTo(this);
            _game.NewMatchCommand.BindTo(_buttonNewMatch).AddTo(this);
            _game.ContinueCommand.BindTo(_buttonContinue).AddTo(this);

            _inputCardCode.text = "DsplVyPmaNUckAuoztBwnTSZgKYOXeFLiIEJqHMdxrfbhCQWjRvG";
        }

        protected override void OnOrientationChanged(bool isLandscape)
        {
            _panelRect.offsetMin = isLandscape ? new Vector2(150, 100) : new Vector2(150, 100);
            _panelRect.offsetMax = isLandscape ? new Vector2(-150, -100) : new Vector2(-150, -100);

            var size = _rectCardCode.sizeDelta;
            size.y = isLandscape ? 70 : 140;
            _rectCardCode.sizeDelta = size;
            _rectCardCode.anchoredPosition = new Vector2(
                _rectCardCode.anchoredPosition.x,
                isLandscape ? 100 : 270
            );

            size = _rectRestart.sizeDelta;
            size.y = isLandscape ? 70 : 140;
            _rectRestart.sizeDelta = size;
            _rectRestart.anchoredPosition = new Vector2(
                _rectRestart.anchoredPosition.x,
                isLandscape ? 0 : 100
            );

            _rectNewMatch.sizeDelta = size;
            _rectNewMatch.anchoredPosition = new Vector2(
                _rectNewMatch.anchoredPosition.x,
                isLandscape ? -100 : -70
            );

            _rectContinue.sizeDelta = size;
            _rectContinue.anchoredPosition = new Vector2(
                _rectContinue.anchoredPosition.x,
                isLandscape ? -200 : -240
            );
        }

        private void OnCardCodeChange(string content)
        {
            _game.CardCode.Value = content;
        }
    }
}