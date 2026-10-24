//+------------------------------------------------------------------+
//| IDC HVN Profile (MetaTrader 5)                                   |
//| Clean-room replica of the order-flow tool shown in ProfitPro's   |
//| videos; not their code. Not a buy/sell signal.                   |
//|                                                                  |
//| Volume profile per period (default 5 minutes); its High Volume   |
//| Nodes (smoothed profile >= threshold % of the peak, clustered)   |
//| become zones from the period start. Biggest node orange, others  |
//| slate blue. Label "N | P%": N = volume at the node's POC, P =    |
//| share of the dominant side (green buyers / red sellers).         |
//| Zones extend until price trades back into them.                  |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "High volume nodes per period as support/resistance zones until touched (tick footprint)."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpMaxBars      = 3000;           // Bars to load
input group "Profile"
input ENUM_TIMEFRAMES  InpPeriod       = PERIOD_M5;      // Profile timeframe
input int              InpRowPoints    = 0;              // Row size in points (0 = auto)
input int              InpBasePoints   = 0;              // Footprint row in points (0 = auto)
input int              InpTargetRows   = 60;             // Auto: rows per profile
input group "High volume nodes"
input double           InpThreshold    = 70;             // HVN threshold %
input int              InpSmoothing    = 2;              // Smoothing passes
input double           InpSensitivity  = 25;             // Cluster sensitivity %
input int              InpMinRows      = 2;              // Min cluster size (rows)
input int              InpGap          = 1;              // Gap tolerance (rows)
input int              InpMaxClusters  = 3;              // Max clusters per period
input int              InpGraceBars    = 0;              // Touch grace bars
input group "Display"
input color            InpPrimary      = C'255,165,0';   // Primary zone
input color            InpSecondary    = C'110,125,160'; // Other zones
input int              InpOpacity      = 22;             // Zone opacity %
input bool             InpKeepTouched  = true;           // Keep touched zones
input bool             InpShowLabels   = true;           // Show labels
input color            InpLabelColor   = clrWhite;       // Label number
input color            InpBuyPct       = C'46,160,110';  // Buyers %
input color            InpSellPct      = C'192,74,68';   // Sellers %
input int              InpLabelSize    = 8;              // Label size
input bool             InpLabelPoc     = true;           // Label value: POC volume (false = cluster volume)
input bool             InpShowPoc      = false;          // Show POC line
input color            InpPocColor     = C'218,165,32';  // POC color
input int              InpMaxZones     = 300;            // Max zones on chart
input group "Alerts"
input bool             InpAlertTouch   = true;           // Alert when price touches a zone

#define PFX "IDChvn_"

struct HvnZone
  {
   int               id;
   int               startBar;
   int               periodEndBar;
   int               endBar;      // -1 while active
   bool              primary;
   double            bottom;
   double            top;
   double            poc;
   double            label;
   double            buyShare;    // 0..1
  };

class CHvnProfile : public CIdcFeed
  {
public:
   CIdcFootprint     bar_;        // forming bar (base rows)
   CIdcFootprint     per;         // current period (base rows)
   datetime          perKey;      // start time of the current period (0 none)
   int               perStart;
   int               perEnd;
   HvnZone           zones[];
   int               nextId;
   double            tick;

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, PFX);
      ArrayResize(zones, 0);
      nextId = 0;
      bar_.Clear();
      per.Clear();
      perKey = 0;
      perStart = -1;
      perEnd = -1;
     }

   datetime          KeyOf(const datetime t)
     {
      int shift = iBarShift(_Symbol, InpPeriod, t, false);
      if(shift < 0)
         return (datetime)((long)t / PeriodSeconds(InpPeriod) * PeriodSeconds(InpPeriod));
      return iTime(_Symbol, InpPeriod, shift);
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      bar_.Add(t.price, t.volume, t.side);
      string msg = "";
      for(int i = ArraySize(zones) - 1; i >= 0; i--)
        {
         if(zones[i].endBar >= 0 || bar <= zones[i].periodEndBar + InpGraceBars)
            continue;
         if(t.price < zones[i].bottom - tick * 0.25 || t.price > zones[i].top - tick * 0.75)
            continue;
         zones[i].endBar = bar;
         msg += DoubleToString(zones[i].bottom, _Digits) + "-" + DoubleToString(zones[i].top - tick, _Digits) + "  ";
         if(!InpKeepTouched)
            DeleteZone(i);
         else
            Draw(i, time, rates_total);
        }
      if(live && InpAlertTouch && StringLen(msg) > 0)
         Alert(_Symbol + " touched HVN zone " + msg);
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      datetime key = KeyOf(time[bar]);
      if(perKey != 0 && key != perKey)
         Finalize(time, rates_total);
      if(perKey == 0)
        {
         perKey = key;
         perStart = bar;
         per.Clear();
        }
      for(int i = 0; i < bar_.Rows(); i++)
        {
         double p = bar_.RowLow(i) + bar_.rowSize * 0.5;
         if(bar_.buy[i] > 0)
            per.Add(p, bar_.buy[i], 1);
         if(bar_.sell[i] > 0)
            per.Add(p, bar_.sell[i], -1);
        }
      perEnd = bar;
      bar_.Clear();
      // the period ends with this bar: close it now instead of waiting for the next period's first trade
      if(time[bar] + PeriodSeconds() >= perKey + PeriodSeconds(InpPeriod) && PeriodSeconds(InpPeriod) < 86400)
         Finalize(time, rates_total);
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      for(int i = 0; i < ArraySize(zones); i++)
         if(zones[i].endBar < 0)
            Draw(i, time, rates_total);
     }

   void              Finalize(const datetime &time[], const int rates_total)
     {
      if(per.Rows() > 0)
         Analyze(time, rates_total);
      per.Clear();
      perKey = 0;
      perStart = -1;
     }

   //--- profile rows, smoothing, clusters (largest first) -> zones
   void              Analyze(const datetime &time[], const int rates_total)
     {
      int rows = per.Rows();
      // profile row: a size in ticks rounded up to whole base rows (as in the TradingView version), rt = base rows per row
      int baseTicks = (int)MathMax(1, MathRound(per.rowSize / tick));
      int rawTicks = InpRowPoints > 0 ? (int)MathMax(1, MathRound(InpRowPoints * _Point / tick)) :
                     (int)MathMax(1, MathCeil(((rows - 1) * baseTicks + 1) / (double)MathMax(5, InpTargetRows)));
      int rt = (int)MathCeil(rawTicks / (double)baseTicks);
      int n = (rows + rt - 1) / rt;
      double vol[], buy[], sell[], sm[], nxt[];
      ArrayResize(vol, n);
      ArrayResize(buy, n);
      ArrayResize(sell, n);
      ArrayInitialize(vol, 0);
      ArrayInitialize(buy, 0);
      ArrayInitialize(sell, 0);
      for(int k = 0; k < rows; k++)
        {
         int r = k / rt;
         vol[r] += per.buy[k] + per.sell[k];
         buy[r] += per.buy[k];
         sell[r] += per.sell[k];
        }
      ArrayResize(sm, n);
      ArrayCopy(sm, vol);
      for(int pass = 0; pass < InpSmoothing; pass++)
        {
         ArrayResize(nxt, n);
         for(int r = 0; r < n; r++)
           {
            double s = 2 * sm[r], w = 2;
            if(r > 0)
              {
               s += sm[r - 1];
               w += 1;
              }
            if(r < n - 1)
              {
               s += sm[r + 1];
               w += 1;
              }
            nxt[r] = s / w;
           }
         ArrayCopy(sm, nxt);
        }
      double mx = sm[ArrayMaximum(sm)];
      if(mx <= 0)
         return;
      double thr = mx * InpThreshold / 100.0;
      // candidate clusters
      int cFirst[], cLast[];
      double cVol[];
      int row = 0;
      while(row < n)
        {
         if(sm[row] < thr)
           {
            row++;
            continue;
           }
         int first = row, last = row, gap = 0;
         for(int r = row + 1; r < n; r++)
           {
            if(sm[r] >= thr)
              {
               last = r;
               gap = 0;
              }
            else
              {
               gap++;
               if(gap > InpGap)
                  break;
              }
           }
         row = last + 1;
         double peak = 0;
         for(int r = first; r <= last; r++)
            peak = MathMax(peak, sm[r]);
         double edge = thr + (peak - thr) * InpSensitivity / 100.0;
         int uFirst = first, uLast = last;
         while(first < last && sm[first] < edge)
            first++;
         while(last > first && sm[last] < edge)
            last--;
         while(last - first + 1 < InpMinRows && (first > uFirst || last < uLast))
           {
            if(first > uFirst && (last >= uLast || sm[first - 1] >= sm[last + 1]))
               first--;
            else
               last++;
           }
         if(last - first + 1 < InpMinRows)
            continue;
         int c = ArraySize(cFirst);
         ArrayResize(cFirst, c + 1);
         ArrayResize(cLast, c + 1);
         ArrayResize(cVol, c + 1);
         cFirst[c] = first;
         cLast[c] = last;
         cVol[c] = 0;
         for(int r = first; r <= last; r++)
            cVol[c] += vol[r];
        }
      // largest first, at most InpMaxClusters
      int count = ArraySize(cFirst);
      bool used[];
      ArrayResize(used, count);
      for(int c = 0; c < count; c++)
         used[c] = false;
      for(int rank = 0; rank < MathMin(count, InpMaxClusters); rank++)
        {
         int best = -1;
         for(int c = 0; c < count; c++)
            if(!used[c] && (best < 0 || cVol[c] > cVol[best]))
               best = c;
         if(best < 0)
            break;
         used[best] = true;
         double b = 0, s = 0, pocVol = 0;
         int pocTick = cFirst[best] * rt;
         for(int r = cFirst[best]; r <= cLast[best]; r++)
           {
            b += buy[r];
            s += sell[r];
           }
         for(int k = cFirst[best] * rt; k < MathMin(rows, (cLast[best] + 1) * rt); k++)
            if(per.buy[k] + per.sell[k] > pocVol)
              {
               pocVol = per.buy[k] + per.sell[k];
               pocTick = k;
              }
         int z = ArraySize(zones);
         ArrayResize(zones, z + 1);
         zones[z].id = nextId++;
         zones[z].startBar = perStart;
         zones[z].periodEndBar = perEnd;
         zones[z].endBar = -1;
         zones[z].primary = rank == 0;
         zones[z].bottom = per.RowLow(cFirst[best] * rt);
         zones[z].top = per.RowLow(MathMin(rows - 1, (cLast[best] + 1) * rt - 1)) + per.rowSize;
         zones[z].poc = per.RowLow(pocTick);
         zones[z].label = InpLabelPoc ? pocVol : cVol[best];
         zones[z].buyShare = b + s > 0 ? b / (b + s) : 0.5;
         Draw(z, time, rates_total);
        }
      while(ArraySize(zones) > InpMaxZones)
        {
         int drop = 0;
         for(int i = 0; i < ArraySize(zones); i++)
            if(zones[i].endBar >= 0)
              {
               drop = i;
               break;
              }
         DeleteZone(drop);
        }
     }

   void              DeleteZone(const int i)
     {
      string id = IntegerToString(zones[i].id);
      ObjectDelete(0, PFX + "z" + id);
      ObjectDelete(0, PFX + "n" + id);
      ObjectDelete(0, PFX + "p" + id);
      ObjectDelete(0, PFX + "l" + id);
      ArrayRemove(zones, i, 1);
     }

   void              Draw(const int i, const datetime &time[], const int rates_total)
     {
      int last = zones[i].endBar >= 0 ? zones[i].endBar : rates_total - 1;
      if(last >= rates_total)
         last = rates_total - 1;
      if(zones[i].startBar < 0 || zones[i].startBar >= rates_total)
         return;
      datetime t1 = time[zones[i].startBar];
      datetime t2 = time[last] + PeriodSeconds();
      string id = IntegerToString(zones[i].id);
      color base = zones[i].primary ? InpPrimary : InpSecondary;
      IdcRect(PFX + "z" + id, t1, zones[i].top, t2, zones[i].bottom, IdcBlend(base, InpOpacity / 100.0));
      if(InpShowLabels)
        {
         bool buyers = zones[i].buyShare >= 0.5;
         int pct = (int)MathMax(51, MathRound(100 * (buyers ? zones[i].buyShare : 1 - zones[i].buyShare)));
         string pctText = " | " + IntegerToString(pct) + "%";
         string num = IdcVolText(zones[i].label, false);
         for(int k = 0; k < StringLen(pctText); k++)
            num += " ";
         double mid = (zones[i].top + zones[i].bottom) / 2;
         // two texts in the same monospace font: the number in white, " | P%" in the dominant side's colour
         IdcText(PFX + "n" + id, t2, mid, num, InpLabelColor, InpLabelSize, "Courier New Bold", ANCHOR_RIGHT, false);
         IdcText(PFX + "p" + id, t2, mid, pctText, buyers ? InpBuyPct : InpSellPct, InpLabelSize, "Courier New Bold", ANCHOR_RIGHT, false);
        }
      if(InpShowPoc && zones[i].primary)
         IdcLine(PFX + "l" + id, t1, zones[i].poc + per.rowSize * 0.5, t2, zones[i].poc + per.rowSize * 0.5, InpPocColor, 2, STYLE_SOLID, false);
     }
  };

CHvnProfile g_ind;

int OnInit()
  {
   if(PeriodSeconds(InpPeriod) <= PeriodSeconds())
      Print("IDC HVN Profile: choose a profile timeframe higher than the chart timeframe");
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(g_ind.tick <= 0)
      g_ind.tick = _Point;
   // base rows: the input, or one tick until OnCalculate sizes them from the bars
   double base = InpBasePoints > 0 ? MathMax(1, MathRound(InpBasePoints * _Point / g_ind.tick)) * g_ind.tick : g_ind.tick;
   g_ind.bar_.rowSize = base;
   g_ind.per.rowSize = base;
   g_ind.nextId = 0;
   g_ind.perKey = 0;
   EventSetTimer(1);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   ObjectsDeleteAll(0, PFX);
   Comment("");
  }

void OnTimer()
  {
   g_ind.Timer();
  }

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
  {
   // automatic base row from the chart's bars, set at every (re)start
   if(prev_calculated == 0 && InpBasePoints <= 0)
     {
      g_ind.bar_.rowSize = IdcAutoRow(high, low, rates_total, g_ind.tick);
      g_ind.per.rowSize = g_ind.bar_.rowSize;
     }
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC HVN Profile: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
