using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Solitaire.Commands;
using Solitaire.Helpers;
using Solitaire.Services;
using UniRx;
using UnityEngine;
using Zenject;
using Random = UnityEngine.Random;

namespace Solitaire.Models
{
    public class Game : DisposableEntity
    {
        public enum Popup
        {
            None,
            Match,
            Options,
            Leaderboard
        }

        public enum State
        {
            Home,
            Dealing,
            Playing,
            Paused,
            Win
        }

        [Inject] private readonly IAudioService _audioService;

        [Inject] private readonly Card.Config _cardConfig;

        [Inject] private readonly ICardSpawner _cardSpawner;

        [Inject] private readonly ICommandService _commandService;

        [Inject] private readonly DrawCardCommand.Factory _drawCardCommandFactory;

        [Inject] private readonly GamePopup _gamePopup;

        [Inject] private readonly GameState _gameState;

        [Inject] private readonly IHintService _hintService;

        [Inject] private readonly Leaderboard _leaderboard;

        [Inject] private readonly MoveCardCommand.Factory _moveCardCommandFactory;

        [Inject] private readonly IMovesService _movesService;

        [Inject] private readonly IPointsService _pointsService;

        [Inject] private readonly RefillStockCommand.Factory _refillStockCommandFactory;

        public Game()
        {
            HasStarted = new BoolReactiveProperty(false);

            CardCode = new StringReactiveProperty();

            RestartCommand = new ReactiveCommand();
            RestartCommand.Subscribe(_ => Restart()).AddTo(this);

            NewMatchCommand = new ReactiveCommand();
            NewMatchCommand.Subscribe(_ => NewMatch()).AddTo(this);

            ContinueCommand = new ReactiveCommand(HasStarted);
            ContinueCommand.Subscribe(_ => Continue()).AddTo(this);
        }

        public BoolReactiveProperty HasStarted { get; }
        public StringReactiveProperty CardCode { get; }
        public ReactiveCommand RestartCommand { get; }
        public ReactiveCommand NewMatchCommand { get; }
        public ReactiveCommand ContinueCommand { get; }

        public Pile PileStock { get; private set; }
        public Pile PileWaste { get; private set; }
        public IList<Pile> PileFoundations { get; private set; }
        public IList<Pile> PileTableaus { get; private set; }
        public List<Card> CacheCards { get; private set; }
        public List<Card> Cards { get; private set; }

        public void Init(
            Pile pileStock,
            Pile pileWaste,
            IList<Pile> pileFoundations,
            IList<Pile> pileTableaus
        )
        {
            PileStock = pileStock;
            PileWaste = pileWaste;
            PileFoundations = pileFoundations;
            PileTableaus = pileTableaus;

            SpawnCards();
            LoadLeaderboard();
        }

        public void RefillStock()
        {
            if (PileStock.HasCards || !PileWaste.HasCards)
            {
                PlayErrorSfx();
                return;
            }

            // Refill stock pile from waste pile
            var command = _refillStockCommandFactory.Create(PileStock, PileWaste);
            command.Execute();
            _commandService.Add(command);
            _movesService.Increment();
        }

        public void MoveCard(Card card, Pile pile)
        {
            if (card == null)
                return;

            // Try to find valid move for the card
            if (pile == null)
                pile = _hintService.FindValidMove(card);

            // Couldn't find move
            if (pile == null)
            {
                card.Shake();
                PlayErrorSfx();
                return;
            }

            // Move card to pile
            var command = _moveCardCommandFactory.Create(card, card.Pile, pile);
            command.Execute();
            _commandService.Add(command);
            _movesService.Increment();
        }

        public void DrawCard()
        {
            // Draw card(s) from stock
            var command = _drawCardCommandFactory.Create(PileStock, PileWaste);
            command.Execute();
            _commandService.Add(command);
            _movesService.Increment();
        }

        public void PlayErrorSfx()
        {
            _audioService.PlaySfx(Audio.SfxError, 0.5f);
        }

        public void DetectWinCondition()
        {
            // All cards in the tableau piles should be revelead
            for (var i = 0; i < PileTableaus.Count; i++)
            {
                var pileTableau = PileTableaus[i];

                for (var j = 0; j < pileTableau.Cards.Count; j++)
                    if (!pileTableau.Cards[j].IsFaceUp.Value)
                        return;
            }

            // The stock and waste piles should be empty
            // if (PileStock.HasCards || PileWaste.HasCards)
            //     return;

            WinAsync().Forget();
        }

        public async UniTask TryShowHintAsync()
        {
            // Try to get hint
            var hint = _hintService.GetHint();

            if (hint == null)
                return;

            // Make copies of the original card and all cards above it
            var pile = hint.Pile;
            var cardsToCopy = hint.Card.Pile.SplitAt(hint.Card);
            var copies = _cardSpawner.MakeCopies(cardsToCopy);

            // Initialize copies without adding them to the pile
            for (var i = 0; i < copies.Count; i++)
            {
                // Calculate order
                var copy = copies[i];
                var index = pile.Cards.Count + i;
                copy.Card.Order.Value = index;

                // Calculate position
                var count = pile.Cards.Count + 1 + i;
                var prevCard =
                    i > 0
                        ? copies[i - 1].Card
                        : pile.HasCards
                            ? pile.Cards[index - 1]
                            : null;
                copy.Card.Position.Value = pile.CalculateCardPosition(index, count, prevCard);
            }

            _audioService.PlaySfx(Audio.SfxHint, 0.5f);

            // Wait until the animation completes
            var delayMs = (int) (_cardConfig.AnimationDuration * 1000) + 50;
            await UniTask.Delay(delayMs);

            // Despawn copies
            for (var i = 0; i < copies.Count; i++)
                _cardSpawner.Despawn(copies[i]);

            await UniTask.Delay(delayMs);
        }

        private void Reset()
        {
            // Reset piles
            PileStock.Reset();
            PileWaste.Reset();

            for (var i = 0; i < PileFoundations.Count; i++)
                PileFoundations[i].Reset();

            for (var i = 0; i < PileTableaus.Count; i++)
                PileTableaus[i].Reset();

            // Reset cards
            for (var i = 0; i < Cards.Count; i++)
                Cards[i].Reset(PileStock.Position);

            // Reset services
            _movesService.Reset();
            _pointsService.Reset();
            _commandService.Reset();

            HasStarted.Value = true;
            _gamePopup.State.Value = Popup.None;
        }

        private void ShuffleCards()
        {
            //// Shuffle by sorting
            //Cards = Cards.OrderBy(a => Guid.NewGuid()).ToList();

            // Fisher-Yates shuffle
            for (var i = Cards.Count - 1; i > 0; i--)
            {
                var n = Random.Range(0, i + 1);
                (Cards[i], Cards[n]) = (Cards[n], Cards[i]);
            }
        }

        private void DecodeCardCode(List<Card> paramIn, List<Card> paramOut, string content)
        {
            paramOut.Clear();
            if (string.IsNullOrEmpty(content))
            {
                paramOut.AddRange(paramIn);
                Debug.LogError("Card data is empty");
                return;
            }

            var array = content.ToCharArray();
            foreach (var data in array)
            {
                int index = GetCardIndex(data);
                paramOut.Add(paramIn[index]);
            }

            paramOut.Reverse();
        }

        private int GetCardIndex(char card)
        {
            // card from A to z;

            if (card >= 97)
            {
                return card - 97; // a = 97
            }

            return card + 26 - 65; //A = 65
        }

        private async UniTask DealAsync()
        {
            // Start dealing
            _gameState.State.Value = State.Dealing;
            var delayMs = (int) (_cardConfig.AnimationDuration * 1000) + 50;

            _audioService.PlaySfx(Audio.SfxShuffle, 0.5f);
            await UniTask.Delay(delayMs);

            // Add cards to the stock pile
            PileStock.AddCards(Cards);

            _audioService.PlaySfx(Audio.SfxDeal, 1.0f);
            await UniTask.Delay(delayMs);

            // Deal cards to the Tableau piles
            for (var i = 0; i < PileTableaus.Count; i++)
            for (var j = 0; j < i + 1; j++)
            {
                var topCard = PileStock.TopCard();
                PileTableaus[i].AddCard(topCard);

                if (j == i)
                    topCard.Flip();

                await UniTask.DelayFrame(3);
            }

            // Start playing
            _gameState.State.Value = State.Playing;
        }

        private async UniTask WinAsync()
        {
            // Start win sequence
            _gameState.State.Value = State.Win;
            HasStarted.Value = false;

            int cardsInTableaus;

            do
            {
                cardsInTableaus = 0;

                // Check each tableau pile
                for (var i = 0; i < CacheCards.Count; i++)
                {
                    var theCard = CacheCards[i];
                    if (theCard.Pile.IsFoundation)
                    {
                        continue;
                    }

                    cardsInTableaus++;

                    // Skip card that cannot be moved to a foundation pile
                    var pileFoundation = GetFoundationBySuit(theCard);
                    if (pileFoundation == null)
                        continue;

                    if (theCard.IsFaceUp.Value == false)
                    {
                        theCard.Flip();
                    }

                    // Move card to the foundation
                    MoveCard(theCard, pileFoundation);
                    await UniTask.DelayFrame(3);
                }
            } while (cardsInTableaus > 0);

            // do
            // {
            //     cardsInTableaus = 0;
            //
            //     // Check each tableau pile
            //     for (var i = 0; i < PileTableaus.Count; i++)
            //     {
            //         var pileTableau = PileTableaus[i];
            //         var topCard = pileTableau.TopCard();
            //         cardsInTableaus += pileTableau.Cards.Count;
            //
            //         // Skip empty pile
            //         if (topCard == null)
            //             continue;
            //
            //         // Skip card that cannot be moved to a foundation pile
            //         var pileFoundation = _hintService.CheckPilesForMove(PileFoundations, topCard);
            //
            //         if (pileFoundation == null)
            //             continue;
            //
            //         // Move card to the foundation
            //         MoveCard(topCard, pileFoundation);
            //         await UniTask.DelayFrame(3);
            //     }
            // } while (cardsInTableaus > 0);

            AddPointsAndSaveLeaderboard();
        }

        private Pile GetFoundationBySuit(Card card)
        {
            foreach (var foundation in PileFoundations)
            {
                if (foundation.HasCards)
                {
                    if (card.Suit == foundation.TopCard().Suit)
                    {
                        return foundation;
                    }
                }
                else
                {
                    if (card.Type == Card.Types.Ace)
                    {
                        return foundation;
                    }
                }
            }

            return null;
        }

        private void Restart()
        {
            Reset();
            DealAsync().Forget();
        }

        private void NewMatch()
        {
            Reset();
            if (string.IsNullOrEmpty(CardCode.Value))
            {
                ShuffleCards();
            }
            else
            {
                DecodeCardCode(CacheCards, Cards, CardCode.Value);
            }

            DealAsync().Forget();
        }

        private void Continue()
        {
            _gameState.State.Value = State.Playing;
            _gamePopup.State.Value = Popup.None;
        }

        private void SpawnCards()
        {
            _cardSpawner.SpawnAll();
            var spawnCards = _cardSpawner.Cards.Select(c => c.Card).ToList();
            CacheCards = new List<Card>(spawnCards);
            Cards = new List<Card>(spawnCards);
        }

        private void AddPointsAndSaveLeaderboard()
        {
            var item = new Leaderboard.Item
            {
                Points = _pointsService.Points.Value,
                Date = DateTime.Now.ToString("HH:mm MM/dd/yyyy")
            };

            _leaderboard.Add(item);
            _leaderboard.Save();
        }

        private void LoadLeaderboard()
        {
            _leaderboard.Load();
        }

        [Serializable]
        public class Config
        {
            public int PointsWasteToTableau = 5;
            public int PointsWasteToFoundation = 10;
            public int PointsTableauToFoundation = 10;
            public int PointsTurnOverTableauCard = 5;
            public int PointsFoundationToTableau = -15;
            public int PointsRecycleWaste = -100;
        }
    }
}