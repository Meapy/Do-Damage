using Shared;
using Xunit;

namespace Tests
{
    public class VisitTests
    {
        private static VisitRules.Place Dungeon() => new VisitRules.Place { Kind = VisitRules.Kind.Instance, Name = "Snake Pit" };

        private static string Ask(VisitBook book, int visitor, int target, long now, out VisitBook.Request r) =>
            book.Ask(visitor, $"V{visitor}", target, $"T{target}", 1000, now, out r);

        [Fact]
        public void DungeonsAndTheArenaCanBeVisited()
        {
            Assert.Null(VisitRules.Refusal(Dungeon(), "Bob"));
        }

        [Theory]
        [InlineData(VisitRules.Kind.Nexus)]
        [InlineData(VisitRules.Kind.Realm)]
        [InlineData(VisitRules.Kind.Vault)]
        [InlineData(VisitRules.Kind.GuildHall)]
        [InlineData(VisitRules.Kind.Test)]
        [InlineData(VisitRules.Kind.Shared)]
        [InlineData(VisitRules.Kind.Gone)]
        public void OtherPlacesAreRefused(VisitRules.Kind kind)
        {
            var p = Dungeon();
            p.Kind = kind;
            Assert.NotNull(VisitRules.Refusal(p, "Bob"));
        }

        [Fact]
        public void NeverBackIntoAnInstanceYouveBeenIn()
        {
            var p = Dungeon();
            p.BeenIn = true;
            Assert.Contains("already been in", VisitRules.Refusal(p, "Bob"));
        }

        [Fact]
        public void FullSingleAndSameAreRefused()
        {
            var p = Dungeon();
            p.Full = true;
            Assert.Contains("full", VisitRules.Refusal(p, "Bob"));
            p = Dungeon();
            p.SinglePlayer = true;
            Assert.NotNull(VisitRules.Refusal(p, "Bob"));
            p = Dungeon();
            p.Same = true;
            Assert.Contains("already with", VisitRules.Refusal(p, "Bob"));
        }

        [Fact]
        public void OneRequestPerVisitorAndACooldown()
        {
            var book = new VisitBook();
            Assert.Null(Ask(book, 1, 2, 0, out var r));
            Assert.NotNull(r);
            Assert.Contains("already waiting", Ask(book, 1, 3, 1000, out _));
            // answered: still the cooldown before the next
            Assert.Same(r, book.Answer(2, r.Id, true, 2000));
            Assert.Contains("before asking again", Ask(book, 1, 3, 5000, out _));
            Assert.Null(Ask(book, 1, 3, VisitBook.CooldownMs, out _));
        }

        [Fact]
        public void APlayerIsAskedByOneAtATime()
        {
            var book = new VisitBook();
            Assert.Null(Ask(book, 1, 9, 0, out _));
            Assert.Contains("another visit request", Ask(book, 2, 9, 100, out _));
        }

        [Fact]
        public void ANoKeepsThatVisitorAwayForAWhile()
        {
            var book = new VisitBook();
            Ask(book, 1, 2, 0, out var r);
            Assert.Same(r, book.Answer(2, r.Id, false, 1000));
            Assert.Contains("didn't let you in", Ask(book, 1, 2, 1000 + VisitBook.CooldownMs, out _));
            Assert.Null(Ask(book, 1, 2, 1000 + VisitBook.DeclinedMs, out _));
        }

        [Fact]
        public void OnlyTheOneAskedCanAnswerAndOnlyOnce()
        {
            var book = new VisitBook();
            Ask(book, 1, 2, 0, out var r);
            Assert.Null(book.Answer(3, r.Id, true, 10));
            Assert.Same(r, book.Answer(2, r.Id, true, 10));
            Assert.Null(book.Answer(2, r.Id, true, 20));
            Assert.False(book.Any);
        }

        [Fact]
        public void ARequestTimesOutAsANoToldToBothSidesOnce()
        {
            var book = new VisitBook();
            Ask(book, 1, 2, 0, out var r);
            Assert.Empty(book.TakeTimedOut(2, VisitBook.TimeoutMs - 1));
            Assert.Null(book.Answer(2, r.Id, true, VisitBook.TimeoutMs));

            var target = book.TakeTimedOut(2, VisitBook.TimeoutMs + 100);
            Assert.Single(target);
            Assert.False(target[0].AsVisitor);
            Assert.Empty(book.TakeTimedOut(2, VisitBook.TimeoutMs + 200));
            var visitor = book.TakeTimedOut(1, VisitBook.TimeoutMs + 300);
            Assert.Single(visitor);
            Assert.True(visitor[0].AsVisitor);
            Assert.False(book.Any);
            Assert.Contains("didn't let you in", Ask(book, 1, 2, VisitBook.TimeoutMs + VisitBook.CooldownMs, out _));
        }

        [Fact]
        public void YesAndNoFindTheRequestWaiting()
        {
            var book = new VisitBook();
            Assert.Null(book.WaitingFor(2, 0));
            Ask(book, 1, 2, 0, out var r);
            Assert.Same(r, book.WaitingFor(2, 10));
            Assert.Null(book.WaitingFor(1, 10));
            Assert.Null(book.WaitingFor(2, VisitBook.TimeoutMs));
        }

        [Fact]
        public void TakingBackFreesTheVisitor()
        {
            var book = new VisitBook();
            Ask(book, 1, 2, 0, out var r);
            Assert.Same(r, book.Withdraw(1));
            Assert.Null(book.Withdraw(1));
            Assert.Null(book.Answer(2, r.Id, true, 10));
            Assert.False(book.Any);
        }
    }
}
