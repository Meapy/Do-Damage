using System.Timers;
using WorldServer.core.market;

namespace WorldServer.core
{
    // Ends the Marketplace's listings whose time is up, every minute (Marketplace.Sweep). A world
    // server handing over to a new one stops it.
    public sealed class MarketSweeper
    {
        private Marketplace Marketplace { get; set; }
        private Timer Timer { get; set; }

        public MarketSweeper(Marketplace marketplace)
        {
            Marketplace = marketplace;
            Timer = new Timer(60000);
            Timer.Elapsed += Timer_Elapsed;
        }

        public void Start() => Timer.Start();
        public void Stop() => Timer.Stop();

        private void Timer_Elapsed(object sender, ElapsedEventArgs e) => Marketplace.Sweep();
    }
}
