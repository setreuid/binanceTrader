using Binance.Net;
using Binance.Net.Enums;
using Binance.Net.Objects.Spot;
using Binance.Net.Objects.Spot.MarketData;
using Binance.Net.Objects.Spot.MarketStream;
using Binance.Net.Objects.Spot.SpotData;
using Binance.Net.Objects.Spot.UserStream;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using WebSocketSharp;
using WMPLib;

namespace binanceTrader
{
    public delegate void OnKeyDown(int code);
    public delegate void OnKeyUp(int code, double duration);



    public struct COIN_PRICE
    {
        public decimal bid; // 매수 - 시장가
        public decimal ask; // 매도
        public string updn; // 직전 대비 상승/하락
    }

    public struct COIN_ORDER
    {
        public string order_currency;
		public string payment_currency;
		public string order_id;
		public string order_date;
		public string type;
		public string units;
		public string units_remaining;
		public string price;
    }

    public struct COIN_BALANCE
    {
        public decimal total_usdt;
        public decimal in_use_usdt;
        public decimal available_usdt;
        public decimal total_btc;
        public decimal in_use_btc;
        public decimal available_btc;
    }

    public struct COIN_TICKER
    {
        public decimal units;
        public decimal price;
        public decimal fee;
        public decimal usdt;
    }



    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);



        private const int ABSOLUTE_SIZE = 65535;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const int MOUSEEVENTF_MOVE = 0x01;
        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;



        public struct POINT
        {
            public Int32 x;
            public Int32 y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }



        private static Dictionary<int, double> keys = new Dictionary<int, double>();

        private static ConcurrentDictionary<string, List<decimal>> klines = new ConcurrentDictionary<string, List<decimal>>();

        private static ConcurrentDictionary<string, DateTime> times = new ConcurrentDictionary<string, DateTime>();

        private static ConcurrentDictionary<string, decimal> prices = new ConcurrentDictionary<string, decimal>();

        private long buyId;

        private long sellId;

        private ConcurrentStack<COIN_TICKER> ticker = new ConcurrentStack<COIN_TICKER>();

        private bool isActive = false;

        private string coin = null;

        private COIN_PRICE price = new COIN_PRICE();

        private COIN_BALANCE balance;

        private BinanceSocketClient ws;

        private BinanceClient api;

        private UpdateSubscription sub = null;

        private BinanceExchangeInfo exchange;

        private bool isFetching = false;

        private Mutex m = new Mutex();

        private Mutex w = new Mutex();

        private decimal initUSDT = 0;

        private Random rand = new Random();

        private string[] watch = { "BTCUSDT", "ETHUSDT", "THETAUSDT", "DENTUSDT", "XRPUSDT", "ADAUSDT", "TFUELUSDT", "ANKRUSDT", "STORJUSDT", "UNIUSDT", "ONEUSDT" };

        private DataTable bullData = new DataTable("bull");

        private static ConcurrentDictionary<string, int> bulls = new ConcurrentDictionary<string, int>();

        private static ConcurrentDictionary<string, int> bulls5 = new ConcurrentDictionary<string, int>();

        private long limit = 0;

        private decimal sum = 0;

        private decimal sumFinal = 0;

        private WMPLib.WindowsMediaPlayer player = new WMPLib.WindowsMediaPlayer();

        private bool isAuto = true;

        /**
         * 순환매수매도 (기준 0.5%)
         * 
         * 0: 매수 0%
         * 1: 하락 후 추가 매수1 -0.5%
         * 2: 하락 후 추가 매수2 -0.75% - 이 상태에서 오르면 1로 변경후 매도 -0.5%
         */
        private int status = -1; // -1: 대기

        private decimal eachUSDT = 0; // 한 매매당 금액 (initUSDT / 5.0)

        private decimal eachCoin = 0; // 한 매매당 갯수



        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Hook.SetHook(OnHookKeyDown, OnHookKeyUp);
            Init();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ws != null) ws.Dispose();
        }

        public async void Init()
        {
            ws = new BinanceSocketClient();

            api = new BinanceClient(new BinanceClientOptions
            {
                ApiCredentials = new ApiCredentials("키", "시크릿")
            });

            exchange = api.Spot.System.GetExchangeInfo().Data;

            await currentBalance();
            initUSDT = balance.available_usdt;
            eachUSDT = initUSDT / 3.0M;

            bullData.Columns.Add(new DataColumn("symbol", Type.GetType("System.String")));
            bullData.Columns.Add(new DataColumn("status", Type.GetType("System.String")));

            foreach (var s in watch)
            {
                DataRow row = bullData.NewRow();
                row["symbol"] = s;
                bullData.Rows.Add(row);
            }
            bullData.AcceptChanges();

            dgBull.ItemsSource = bullData.DefaultView;

            ws.Spot.SubscribeToKlineUpdates(watch, KlineInterval.OneMinute, onKline =>
            {
                if (!prices.ContainsKey(onKline.Symbol))
                {
                    prices.TryAdd(onKline.Symbol, onKline.Data.Close);
                }
                else
                {
                    prices[onKline.Symbol] = onKline.Data.Close;
                }

                if (!times.ContainsKey(onKline.Symbol))
                {
                    times.TryAdd(onKline.Symbol, onKline.Data.OpenTime);
                }

                updateKline(onKline.Symbol, onKline.Data.Close - onKline.Data.Open, onKline.Data.OpenTime.ToString() != times[onKline.Symbol].ToString());
                if (onKline.Data.OpenTime.ToString() != times[onKline.Symbol].ToString())
                {
                    times[onKline.Symbol] = onKline.Data.OpenTime;
                }
            });

            //
            var startResult = api.Spot.UserStream.StartUserStream();


            if (!startResult.Success)
                throw new Exception($"Failed to start user stream: {startResult.Error}");

            ws.Spot.SubscribeToUserDataUpdates(
                startResult.Data,
                orderUpdate => {
                    if (orderUpdate.ExecutionType == Binance.Net.Enums.ExecutionType.New)
                    {
                        if (orderUpdate.Side == Binance.Net.Enums.OrderSide.Buy)
                        {
                            buyId = orderUpdate.OrderId;
                        }
                        else
                        {
                            sellId = orderUpdate.OrderId;
                        }
                    }

                    if (orderUpdate.ExecutionType == Binance.Net.Enums.ExecutionType.Canceled)
                    {
                        if (orderUpdate.Side == Binance.Net.Enums.OrderSide.Buy)
                        {
                            buyId = 0;
                        }
                        else
                        {
                            sellId = 0;
                        }
                    }

                    if (orderUpdate.ExecutionType == Binance.Net.Enums.ExecutionType.Trade && orderUpdate.Status == OrderStatus.Filled)
                    {
                        /* if (orderUpdate.Side == OrderSide.Buy && buyId == 0) return;
                        if (orderUpdate.Side == OrderSide.Sell && sellId == 0) return;

                        var p = orderUpdate.Price == 0 ? orderUpdate.LastPriceFilled : orderUpdate.Price;
                        OrderUpdate(orderUpdate.Side, p, orderUpdate.Quantity, orderUpdate.Commission); */
                    }
                },
                ocoUpdate => {
                    Trace.WriteLine(ocoUpdate);
                },
                positionUpdate => {
                    foreach (var item in positionUpdate.Balances)
                    {
                        if (item.Asset.ToUpper() == "USDT")
                        {
                            balance.available_usdt = item.Free;
                            balance.in_use_usdt = item.Locked;
                            balance.total_usdt = item.Total;
                            break;
                        }
                    }
                },
                balanceUpdate => {
                    Trace.WriteLine(balanceUpdate);
                }
            );


            //
            if (isAuto)
            {
                coin = "BTCUSDT";
                isActive = true;
                await subscribeCoin(coin);
                await loadStatus();
            }


            //
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += new EventHandler(timer_Tick);
            timer.Start();

            //
            DispatcherTimer timerPrint = new DispatcherTimer();
            timerPrint.Interval = TimeSpan.FromSeconds(0.1);
            timerPrint.Tick += new EventHandler(timerPrint_Tick);
            timerPrint.Start();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            process();

            dgBull.ItemsSource = null;
            dgBull.ItemsSource = bullData.DefaultView;
        }

        private void timerPrint_Tick(object sender, EventArgs e)
        {
            print();
        }



        private async Task subscribeCoin(string c)
        {
            if (status != -1) return;

            if (sub != null)
            {
                ws.Unsubscribe(sub);
            }

            price.bid = 0;
            price.ask = 0;
            price.updn = string.Empty;

            sub = ws.Spot.SubscribeToBookTickerUpdates(c, data => {
                if (data.Symbol == c)
                {
                    price.updn = (data.BestBidPrice == price.bid ? string.Empty : (data.BestBidPrice > price.bid ? "up" : "dn"));
                    price.ask = data.BestAskPrice;
                    price.bid = data.BestBidPrice; // 매수 가격 - 시장가
                }
            }).Data;
        }

        private void updateGridBull(string symbol, string data)
        {
            for (var i = 0; i < bullData.Rows.Count; i++)
            {
                if (bullData.Rows[i]["symbol"].ToString() == symbol)
                {
                    bullData.Rows[i]["status"] = data;
                    break;
                }
            }
        }

        private void updateKline(string symbol, decimal data, bool isNew)
        {
            if (!klines.ContainsKey(symbol))
            {
                klines.TryAdd(symbol, new List<decimal>());
            }

            if (!bulls.ContainsKey(symbol))
            {
                bulls.TryAdd(symbol, 0);
            }

            if (!bulls5.ContainsKey(symbol))
            {
                bulls5.TryAdd(symbol, 0);
            }

            if (isNew || klines[symbol].Count == 0)
            {
                klines[symbol].Add(data);
            }
            else
            {
                klines[symbol][klines[symbol].Count - 1] = data;
            }

            if (klines[symbol].Count > 2)
            {
                var i = klines[symbol].Count;
                if (klines[symbol][i - 1] > 0 && klines[symbol][i - 2] > 0 && klines[symbol][i - 3] > 0)
                {
                    updateGridBull(symbol, "up");
                    bulls[symbol] = 1;
                }
                else if (klines[symbol][i - 1] < 0 && klines[symbol][i - 2] < 0 && klines[symbol][i - 3] < 0)
                {
                    updateGridBull(symbol, "dn");
                    bulls[symbol] = -1;
                }
                else
                {
                    updateGridBull(symbol, string.Empty);
                    bulls[symbol] = 0;
                }
            }

            if (klines[symbol].Count > 4)
            {
                var i = klines[symbol].Count;
                if (klines[symbol][i - 1] > 0 && klines[symbol][i - 2] > 0 && klines[symbol][i - 3] > 0 && klines[symbol][i - 4] > 0 && klines[symbol][i - 5] > 0)
                {
                    bulls5[symbol] = 1;
                }
                else if (klines[symbol][i - 1] < 0 && klines[symbol][i - 2] < 0 && klines[symbol][i - 3] < 0 && klines[symbol][i - 4] < 0 && klines[symbol][i - 5] < 0)
                {
                    bulls5[symbol] = -1;
                }
                else
                {
                    bulls5[symbol] = 0;
                }
            }
        }



        async private void process()
        {
            if (isFetching) return;
            isFetching = true;
            m.WaitOne();

            if (!isAuto)
            {
                var c = getCoinName(GetActiveWindowTitle());

                if (c != null && (c != coin))
                {
                    await subscribeCoin(c);
                }

                if (c != null && status == -1)
                {
                    coin = c;
                    await forceUpdatePrice();
                }

                if (c == null || coin != c)
                {
                    isActive = false;
                }
                else
                {
                    isActive = true;
                }
            }

            await currentBalance();
            if (isAuto) await autoTrade();
            await checkOrder();
            await autoAsk();
            await checkLimit();

            m.ReleaseMutex();
            isFetching = false;
        }

        async private Task currentBalance()
        {
            var b = api.General.GetAccountInfo();
            if (b.Success)
            {
                foreach (var item in b.Data.Balances)
                {
                    if (item.Asset.ToUpper() == "USDT")
                    {
                        balance.available_usdt = item.Free;
                        balance.in_use_usdt = item.Locked;
                        balance.total_usdt = item.Total;

                        if (status == -1)
                        {
                            eachUSDT = balance.available_usdt / 3.0M;
                        }
                        break;
                    }
                }
            }
        }

        async private Task forceUpdatePrice()
        {
            var result = api.Spot.Market.GetPrice(coin);
            if (result.Success)
            {
                price.bid = result.Data.Price;
            }
        }

        async private Task checkOrder()
        {
            if (buyId != 0)
            {
                var opens = api.Spot.Order.GetOrder(coin, buyId);
                if (opens.Success && opens.Data.Status == OrderStatus.Filled)
                {
                    var o = opens.Data;
                    var p = o.Price == 0 ? o.AverageFillPrice : o.Price;
                    OrderUpdate(OrderSide.Buy, (decimal) p, o.Quantity, 0M);
                }
            }

            if (sellId != 0)
            {
                var opens = api.Spot.Order.GetOrder(coin, sellId);
                if (opens.Success && opens.Data.Status == OrderStatus.Filled)
                {
                    var o = opens.Data;
                    var p = o.Price == 0 ? o.AverageFillPrice : o.Price;
                    OrderUpdate(OrderSide.Sell, (decimal) p, o.Quantity, 0M);
                }
            }
        }

        async private void OrderUpdate(OrderSide side, decimal p, decimal quantity, decimal commission)
        {
            if (side == Binance.Net.Enums.OrderSide.Buy)
            {
                var tk = new COIN_TICKER
                {
                    units = quantity,
                    price = p,
                    fee = commission,
                    usdt = quantity * p,
                };
                ticker.Push(tk);

                buyId = 0;
                if (status == -1)
                {
                    limit = 360; // 30Min
                    eachCoin = quantity;
                }
                status++;
                sum -= quantity * p;

                await forceUpdatePrice();
                _T(string.Format("매수 성공 {0} - {1}: {2} ({3} USDT)", status, coin, tk.units, string.Format("{0:#,###}", tk.usdt)));
                PlaySoundBid();
            }
            else
            {
                sellId = 0;
                sum += p * quantity;
                var cnt = Math.Round(quantity / ticker.First().units);

                for (var i = 0; i < cnt; i++)
                {
                    status--;
                    await TryPopPrice();
                }

                _T(string.Format("매도 성공 {0} - {1}", status, coin));
                PlaySoundAsk();

                if (status == -1)
                {
                    sumFinal += sum;
                    sum = 0;
                    ticker.Clear();
                    limit = 0;
                }

                await forceUpdatePrice();
            }

            await currentBalance();
        }

        async private Task TryPopPrice()
        {
            if (ticker.Count() < 1) return;

            COIN_TICKER ti;
            if (!ticker.TryPop(out ti))
                await TryPopPrice();
        }

        async private Task autoAsk()
        {
            if (status == -1) return;

            var current = getEarnPercent();

            if (current == 0) return;

            if (current >= 5.0M || current <= -1.0M)
            {
                // 5% 익절
                // -1% 손절
                await Sell(1M, true);
            }
            else if (isAuto && current > 0.2M && bulls5.ContainsKey("BTCUSDT") && bulls5["BTCUSDT"] == -1)
            {
                Sell(1M, true);
            }
        }

        async private Task checkLimit()
        {
            if (status != -1 && limit > 0)
            {
                limit--;
                this.Title = string.Format("COIN TRADER - {0} (LT {1})", coin, limit);
                if (limit <= 0)
                {
                    await Sell(1M, true);
                }
            }
            else
            {
                this.Title = string.Format("COIN TRADER - {0}", coin);
            }
        }

        async private Task loadStatus()
        {
            var b = api.General.GetAccountInfo();
            if (b.Success)
            {
                var mc = b.Data.Balances.Where(x => prices.ContainsKey(string.Format("{0}USDT", x.Asset))).OrderByDescending(e => e.Free * prices[string.Format("{0}USDT", e.Asset)]);
                if (mc?.Count() > 0)
                {
                    var maxCoin = mc.First();

                    if (maxCoin.Free * prices[string.Format("{0}USDT", maxCoin.Asset)] < 1M)
                    {
                        // 기본 상태
                        limit = 0;
                        status = -1;
                        sellId = 0;
                        buyId = 0;

                        var opens = api.Spot.Order.GetOpenOrders().Data?.Where(x => x.Side == OrderSide.Buy);
                        if (opens?.Count() > 0)
                        {
                            // 매수 주문 있는 상태
                            var c = opens.First();
                            coin = c.Symbol;
                            buyId = c.OrderId;
                            _T(string.Format("매수 주문 - {0}: {1,10:N5}", c.Symbol, c.Price));
                            if (isAuto) await changeLocationWithSymbol(coin);

                            if (c.Status == OrderStatus.New && (DateTime.Now - c.CreateTime).TotalMinutes > 1)
                            {
                                await CancelAll();
                            }
                        }
                    }
                    else
                    {
                        // 매수 상태
                        coin = string.Format("{0}USDT", maxCoin.Asset);

                        var symbol = exchange.Symbols.First(e => e.Name == coin);
                        var orders = api.Spot.Order.GetMyTrades(coin).Data?.OrderByDescending(x => x.TradeTime);
                        if (orders?.Count() > 0)
                        {
                            var orderUpdate = orders.First();
                            var p = orderUpdate.Price == 0 ? prices[coin] : orderUpdate.Price;
                            status = 0;
                            ticker.Push(new COIN_TICKER
                            {
                                units = maxCoin.Free,
                                price = p,
                                fee = orderUpdate.Commission,
                                usdt = maxCoin.Free * p,
                            });

                            if (limit == 0)
                            {
                                limit = 360;
                            }

                            buyId = 0;
                            sellId = 0;
                            initUSDT = balance.available_usdt + getRUP() * getCount();
                            eachUSDT = initUSDT / 3.0M;
                            eachCoin = maxCoin.Free;

                            var opens = api.Spot.Order.GetOpenOrders().Data?.Where(x => x.Side == OrderSide.Sell);
                            if (opens?.Count() > 0)
                            {
                                // 매도 주문 있는 상태
                                var c = opens.First();
                                coin = c.Symbol;
                                sellId = c.OrderId;
                                _T(string.Format("매도 주문 - {0}: {1,10:N5}", c.Symbol, c.Price));
                                if (isAuto) await changeLocationWithSymbol(coin);

                                if (c.Status == OrderStatus.New && (DateTime.Now - c.CreateTime).TotalMinutes > 1)
                                {
                                    await CancelAll();
                                }
                            }
                        }
                    }
                }
            }
        }

        async private Task autoTrade()
        {
            if (status == -1)
            {
                if (sellId == 0 && buyId == 0)
                {
                    // BTC가 상승장인지
                    if (bulls.ContainsKey("BTCUSDT") && bulls["BTCUSDT"] == 1)
                    {
                        // 하락장 랜덤 코인 선택
                        var coins = bulls.Where(x => x.Value == -1 && x.Key != "BTCUSDT");
                        if (coins.Count() > 0)
                        {
                            var one = coins.ToList()[rand.Next(0, coins.Count())].Key;

                            coin = one;
                            await Buy(1M);

                            if (isAuto) await changeLocationWithSymbol(one);
                        }
                    }
                }
            }
            else
            {
                var per = getEarnPercent();
                var bull = bulls.ContainsKey(coin) ? bulls[coin] : 0;
                var rup = getRUP();

                if (per < 0M)
                {
                    if (bull == 1 && (status == 2 || status == 1) && price.bid > (ticker.First().price * 1.002M))
                    {
                        await Sell(1M);
                    }
                    else if (per < -0.5M && bull == -1 && (status == 0 || status == 1) && price.bid < rup)
                    {
                        await Buy(1M);
                    }
                }
            }
        }

        private async Task print()
        {
            if (!isActive)
            {
                cvMain.Opacity = .8;
            }
            else
            {
                cvMain.Opacity = 1;
            }

            lbCoin.Content = coin;
            lbClosing.Content = string.Format("{0,10:N8}", price.bid);

            if (price.updn == "up")
            {
                lbClosing.Foreground = new SolidColorBrush(Color.FromRgb(46, 189, 133));
                imgUpdn.Source = new BitmapImage(new Uri("pack://application:,,,/binanceTrader;component/Resources/up.png"));
            }
            else if (price.updn == "dn")
            {
                lbClosing.Foreground = new SolidColorBrush(Color.FromRgb(224, 41, 74));
                imgUpdn.Source = new BitmapImage(new Uri("pack://application:,,,/binanceTrader;component/Resources/dn.png"));
            }
            else
            {
                lbClosing.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                imgUpdn.Source = null;
            }

            lbTKrw.Content = string.Format("{0,10:N8}", balance.available_usdt + (status == -1 ? 0 : getRUP() * getCount()));
            lbTBtc.Content = string.Format("{0,10:N8}", balance.available_btc);

            lbBidCoin.Content = string.Format("{0,10:N8}", getCount());
            lbBidKrw.Content = string.Format("{0,10:N8}", getRUP());

            lbEarn.Content = string.Format("{0,10:N8}", getEarn());

            var percent = getEarnPercent();

            if (percent == 0)
            {
                lbPercent.Content = string.Format("{0,10:N2}%", percent);
                lbPercent.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                lbEarn.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            }
            else if (percent > 0)
            {
                lbPercent.Content = string.Format("{0,10:N2}%", percent);
                lbPercent.Foreground = new SolidColorBrush(Color.FromRgb(46, 189, 133));
                lbEarn.Foreground = new SolidColorBrush(Color.FromRgb(46, 189, 133));
            }
            else if (percent < 0)
            {
                lbPercent.Content = string.Format("{0,10:N2}%", percent);
                lbPercent.Foreground = new SolidColorBrush(Color.FromRgb(224, 41, 74));
                lbEarn.Foreground = new SolidColorBrush(Color.FromRgb(224, 41, 74));
            }

            lbSum.Content = string.Format("{0,10:N8}", sumFinal);

            if (sumFinal < 0)
            {
                lbSum.Foreground = new SolidColorBrush(Color.FromRgb(224, 41, 74));
            }
            else
            {
                lbSum.Foreground = new SolidColorBrush(Color.FromRgb(46, 189, 133));
            }
        }



        private void OnHookKeyDown(int code)
        {
            if (!keys.ContainsKey(code))
            {
                keys.Add(code, Hook.GetTime());
            }
        }

        private void OnHookKeyUp(int code, double duration)
        {
            keys.Remove(code);

            if (keys.ContainsKey(164) && code.Equals(49)) // Alt + 1
            {
                Buy(1M);
            }
            else if (keys.ContainsKey(164) && code.Equals(50)) // Alt + 2
            {
                Sell(1M);
            }
            else if (keys.ContainsKey(164) && code.Equals(51)) // Alt + 3
            {
                CancelAll();
            }
            else if (keys.ContainsKey(164) && code.Equals(52)) // Alt + 4
            {
                Buy(1M);
            }
            else if (keys.ContainsKey(164) && code.Equals(53)) // Alt + 5
            {
                status = 0;
                Sell(1M, true);
            }
            else if (keys.ContainsKey(164) && code.Equals(54)) // Alt + 6
            {
                changeRandomLocation();
            }
            else if (keys.ContainsKey(164) && code.Equals(55)) // Alt + 7
            {
                changeBtcLocation();
            }
        }



        // 매수
        private async Task Buy(decimal mul)
        {
            await Buy(mul, eachUSDT);
        }

        private async Task Buy(decimal mul, decimal b)
        {
            w.WaitOne();
            if (buyId != 0 || sellId != 0) return;
            await forceUpdatePrice();

            var binanceSymbol = exchange.Symbols.First(e => e.Name == coin);
            var availableBase = api.General.GetAccountInfo().Data.Balances.First(e => e.Asset == binanceSymbol.QuoteAsset).Free;

            var p = Math.Truncate(RoundDown(price.bid * mul, binanceSymbol.BaseAssetPrecision) / binanceSymbol.PriceFilter.TickSize) * binanceSymbol.PriceFilter.TickSize;
            var quantity = Math.Truncate(RoundDown(eachUSDT / p, binanceSymbol.QuoteAssetPrecision) / binanceSymbol.LotSizeFilter.StepSize) * binanceSymbol.LotSizeFilter.StepSize;

            if (status == -1)
            {
                eachCoin = quantity;
            }
            else
            {
                quantity = eachCoin;
            }

            var quoteQuantity = p * quantity;

            if (quoteQuantity >= binanceSymbol.MinNotionalFilter.MinNotional)
            {
                WebCallResult<BinancePlacedOrder> result = null;

                if (mul == 1.0M)
                {
                    result = api.Spot.Order.PlaceOrder(
                        coin,
                        OrderSide.Buy,
                        OrderType.Market,
                        quantity: quantity);
                }
                else
                {
                    result = api.Spot.Order.PlaceOrder(
                        coin,
                        OrderSide.Buy,
                        OrderType.Limit,
                        quantity: quantity,
                        price: p,
                        timeInForce: TimeInForce.GoodTillCancel);
                }

                if (result.Success)
                {
                    buyId = result.Data.OrderId;
                    _T(string.Format("매수 주문 - {0}: {1,10:N5}", coin, p));

                    if (isAuto)
                    {
                        subscribeCoin(coin);
                    }
                }
                else
                {
                    _T(result.Error.Message);
                }
            }
            w.ReleaseMutex();
        }

        // 매도
        private async Task Sell(decimal mul)
        {
            await Sell(mul, false);
        }

        private async Task Sell(decimal mul, bool isAll)
        {
            w.WaitOne();
            if (buyId != 0 || sellId != 0) return;
            await forceUpdatePrice();

            try
            {
                var binanceSymbol = exchange.Symbols.First(e => e.Name == coin);
                var balances = api.General.GetAccountInfo();

                if (!balances.Success)
                {
                    return;
                }

                var availableBase = balances.Data.Balances.First(e => e.Asset == binanceSymbol.BaseAsset).Free;

                var p = Math.Truncate(RoundDown(price.bid * mul, binanceSymbol.QuoteAssetPrecision) / binanceSymbol.PriceFilter.TickSize) * binanceSymbol.PriceFilter.TickSize;
                var quantity = Math.Truncate(RoundDown(isAll ? availableBase : ticker.First().units, binanceSymbol.BaseAssetPrecision) / binanceSymbol.LotSizeFilter.StepSize) * binanceSymbol.LotSizeFilter.StepSize;

                var quoteQuantity = p * quantity;
                if (quoteQuantity >= binanceSymbol.MinNotionalFilter.MinNotional)
                {
                    WebCallResult<BinancePlacedOrder> result = null;

                    if (mul == 1.0M)
                    {
                        result = api.Spot.Order.PlaceOrder(
                        coin,
                        OrderSide.Sell,
                        OrderType.Market,
                        quantity: quantity);
                    }
                    else
                    {
                        result = api.Spot.Order.PlaceOrder(
                        coin,
                        OrderSide.Sell,
                        OrderType.Limit,
                        quantity: quantity,
                        price: p,
                        timeInForce: TimeInForce.GoodTillCancel);
                    }

                    if (result.Success)
                    {
                        sellId = result.Data.OrderId;
                        _T(string.Format("매도 주문 - {0}: {1,10:N5}", coin, p));
                    }
                    else
                    {
                        _T(result.Error.Message);
                    }
                }
            }
            catch (Exception)
            {
                //
            }
            w.ReleaseMutex();
        }

        private async Task CancelAll()
        {
            w.WaitOne();
            if (buyId != 0)
            {
                if (api.Spot.Order.CancelOrder(coin, buyId).Success)
                {
                    buyId = 0;
                    _T(string.Format("매수 취소 - {0}", coin));
                }
            }

            if (sellId != 0)
            {
                if (api.Spot.Order.CancelOrder(coin, sellId).Success)
                {
                    sellId = 0;
                    _T(string.Format("매도 취소 - {0}", coin));
                }
            }
            w.ReleaseMutex();
        }



        private string getCoinName(string title)
        {
            if (title == null) return null;

            Regex regex = new Regex(@"[0-9,\.]+? \| ([A-Z]+?) \| .+");
            if (regex.IsMatch(title))
            {
                var result = regex.Match(title);
                return result.Groups[1].Value.ToString().Trim();
            }
            else
            {
                return null;
            }
        }

        private decimal getRUP()
        {
            if (status == -1)
            {
                return 0;
            }

            var s = 0M;
            var cnt = 0M;
            foreach (var x in ticker)
            {
                s = ((s * cnt) + (x.units * x.price)) / (cnt + x.units);
                cnt += x.units;
            }

            return s;
        }

        private decimal getCount()
        {
            if (status == -1)
            {
                return 0;
            }

            var cnt = 0M;
            foreach (var x in ticker)
            {
                cnt += x.units;
            }

            return cnt;
        }

        private decimal getEarn()
        {
            if (status == -1)
            {
                return 0;
            }

            var s = 0M;
            foreach (var x in ticker)
            {
                s += x.units * x.price;
            }

            return price.bid * getCount() - s;
        }

        private decimal getEarnPercent()
        {
            if (status == -1)
            {
                return 0;
            }

            var cnt = getCount();

            return (1 - ((getRUP() * cnt) / (price.bid * cnt))) * 100.0M;
        }



        private string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, Buff, nChars) > 0)
            {
                return Buff.ToString();
            }
            return null;
        }

        private decimal RoundDown(decimal number, double decimalPlaces)
        {
            var power = Convert.ToDecimal(Math.Pow(10, decimalPlaces));
            return Math.Floor(number * power) / power;
        }

        private async Task changeLocation(string c)
        {
            if (!isActive) return;
            if (isAuto)
            {
                var ci = getCoinName(GetActiveWindowTitle());
                if (ci != c)
                {
                    return;
                }
            }

            IntPtr handle = GetForegroundWindow();
            System.Windows.Clipboard.SetText(string.Format("https://www.binance.com/ko/trade/{0}_{1}", c, "USDT"));

            LeftClick(handle, 340, 53);
            Thread.Sleep(TimeSpan.FromSeconds(0.3));

            SendKeys.SendWait("^(a)");
            SendKeys.SendWait("^(v)");
            SendKeys.SendWait("{ENTER}");
        }

        private async Task changeRandomLocation()
        {
            // var lst = exchange.Symbols.Where(x => x.QuoteAsset == "USDT").ToArray();
            // var one = lst[rand.Next(0, lst.Length)];

            // System.Windows.Clipboard.SetText(string.Format("https://www.binance.com/ko/trade/{0}_{1}", one.BaseAsset, one.QuoteAsset));

            var one = watch[rand.Next(0, watch.Length)];
            changeLocationWithSymbol(one);
        }

        private async Task changeBtcLocation()
        {
            changeLocation("BTC");
        }

        private async Task changeLocationWithSymbol(string c)
        {
            changeLocation(c.Substring(0, c.Length - 4));
        }

        public static void LeftClick(IntPtr hWnd, int x, int y)
        {
            RECT rect = new RECT();

            GetWindowRect(hWnd, ref rect);
            Point targetPoint = new Point(
                rect.Left + x,
                rect.Top + y
            ); //rect.PointToScreen(new Point(x, y));

            targetPoint.X = (int)(targetPoint.X * ABSOLUTE_SIZE / System.Windows.SystemParameters.PrimaryScreenWidth);
            targetPoint.Y = (int)(targetPoint.Y * ABSOLUTE_SIZE / System.Windows.SystemParameters.PrimaryScreenHeight);

            mouse_event(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE | MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)targetPoint.X, (uint)targetPoint.Y, 0, 0);
        }

        public void PlaySound(string path)
        {
            player.URL = path;
        }

        public void PlaySoundBid()
        {
            PlaySound("bid.mp3");
        }

        public void PlaySoundAsk()
        {
            PlaySound("ask.mp3");
        }

        public void _T(string s)
        {
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() => {
                lbMessage.Content = s;
            }));
        }
    }
}
