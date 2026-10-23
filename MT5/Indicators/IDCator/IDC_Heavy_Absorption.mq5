//+------------------------------------------------------------------+
//| IDC Heavy Absorption (MetaTrader 5)                              |
//| Clean-room replica of the order-flow tool shown in ProfitPro's   |
//| videos; not their code. Not a buy/sell signal.                   |
//|                                                                  |
//| Marks exceptionally heavy aggressive volume at one price inside  |
//| a bar: green circle = buyers, red = sellers. Threshold = mean +  |
//| k standard deviations of the heaviest price per bar and side     |
//| over the lookback window. The number is the aggressive volume at |
//| that price and keeps counting up on the forming bar.             |
//| With exchange trades a single big print can be used instead.    |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "Heavy aggressive volume at one price, marked with a circle that grows with the volume."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

enum ENUM_IDC_HA_UNIT
  {
   IDC_HA_PRICE = 0, // Volume at one price (footprint cell)
   IDC_HA_PRINT = 1  // Single print (exchange trades only)
  };

input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpRowPoints    = 0;              // Row size in points (0 = 1 tick)
input int              InpMaxBars      = 1500;           // Bars to load
input group "Detection"
input ENUM_IDC_HA_UNIT InpUnit         = IDC_HA_PRICE;   // What counts as heavy
input int              InpLookback     = 100;            // Lookback bars
input double           InpK            = 2.0;            // Std dev multiplier
input int              InpMinSamples   = 40;             // Min samples before marking
input double           InpMinVolume    = 0;              // Min volume filter (hide smaller)
input group "Display"
input color            InpBullColor    = C'38,166,91';   // Bull circle
input color            InpBearColor    = C'235,64,52';   // Bear circle
input int              InpFillOpacity  = 32;             // Fill opacity %
input bool             InpRing         = true;           // Ring
input color            InpTextColor    = clrWhite;       // Text color
input int              InpTextSize     = 8;              // Text size
input int              InpMinSize      = 14;             // Min circle size
input int              InpMaxSize      = 34;             // Max circle size
input int              InpMaxMarkers   = 300;            // Max markers on chart
input group "Alerts"
input bool             InpAlert        = false;          // Alert on bar close

#define PFX "IDCha_"

struct HaMarker
  {
   int               id;
   int               bar;
   int               side;
   double            price;
   double            volume;
  };

class CHeavyAbsorption : public CIdcFeed
  {
public:
   CIdcFootprint     fp;
   CIdcWindowStats   stats;
   HaMarker          markers[];
   int               nextId;
   double            rowSize;
   double            tick;
   //--- largest single print of the forming bar per side (print mode)
   double            printBuy, printBuyPx, printSell, printSellPx;
   //--- markers of the forming bar (index into markers[], -1 none)
   int               liveBuy, liveSell;

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, PFX);
      ArrayResize(markers, 0);
      nextId = 0;
      fp.Clear();
      stats.Init(InpLookback);
      printBuy = 0;
      printSell = 0;
      printBuyPx = 0;
      printSellPx = 0;
      liveBuy = -1;
      liveSell = -1;
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      fp.Add(t.price, t.volume, t.side);
      if(t.side > 0 && t.volume > printBuy)
        {
         printBuy = t.volume;
         printBuyPx = t.price;
        }
      if(t.side < 0 && t.volume > printSell)
        {
         printSell = t.volume;
         printSellPx = t.price;
        }
      if(live)
         Evaluate(bar, time);   // realtime: the marker follows the forming bar trade by trade
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      Evaluate(bar, time);
      double bestBuy = 0, bestSell = 0;
      HeaviestRows(bestBuy, bestSell);
      if(InpUnit == IDC_HA_PRINT && UsesTrades())
        {
         bestBuy = printBuy;
         bestSell = printSell;
        }
      if(bestBuy > 0)
         stats.Add(bestBuy);
      if(bestSell > 0)
         stats.Add(bestSell);
      stats.CloseBar();
      if(live && InpAlert)
        {
         string msg = "";
         if(liveBuy >= 0)
            msg += "heavy BUY " + IdcVolText(markers[liveBuy].volume, true) + " @ " + DoubleToString(markers[liveBuy].price, _Digits) + "  ";
         if(liveSell >= 0)
            msg += "heavy SELL " + IdcVolText(markers[liveSell].volume, true) + " @ " + DoubleToString(markers[liveSell].price, _Digits);
         if(StringLen(msg) > 0)
            Alert(_Symbol + " " + msg);
        }
      fp.Clear();
      printBuy = 0;
      printSell = 0;
      liveBuy = -1;
      liveSell = -1;
      while(ArraySize(markers) > InpMaxMarkers)
         DeleteMarker(0);
     }

   void              HeaviestRows(double &bestBuy, double &bestSell)
     {
      bestBuy = 0;
      bestSell = 0;
      for(int i = 0; i < fp.Rows(); i++)
        {
         bestBuy = MathMax(bestBuy, fp.buy[i]);
         bestSell = MathMax(bestSell, fp.sell[i]);
        }
     }

   //--- creates, moves or removes the forming bar's markers
   void              Evaluate(const int bar, const datetime &time[])
     {
      bool ready = stats.Count() >= InpMinSamples;
      double mean = stats.Mean();
      double thr = stats.Threshold(InpK);
      for(int side = 1; side >= -1; side -= 2)
        {
         double best = 0, bestPx = 0, shown = 0;
         if(InpUnit == IDC_HA_PRINT && UsesTrades())
           {
            best = side > 0 ? printBuy : printSell;
            bestPx = side > 0 ? printBuyPx : printSellPx;
            shown = best;
           }
         else
            for(int i = 0; i < fp.Rows(); i++)
              {
               double v = side > 0 ? fp.buy[i] : fp.sell[i];
               if(v > best)
                 {
                  best = v;
                  bestPx = fp.RowLow(i) + (rowSize - tick) / 2;
                 }
              }
         if(InpUnit != IDC_HA_PRINT || !UsesTrades())
            shown = best;
         else
           {
            // the number shows the same-side volume at the print's price, as in the videos
            int row = (int)(fp.RowOf(bestPx) - fp.lowRow);
            if(row >= 0 && row < fp.Rows())
               shown = side > 0 ? fp.buy[row] : fp.sell[row];
           }
         bool qualifies = ready && best >= thr && best > mean && shown >= InpMinVolume && best > 0;
         int idx = side > 0 ? liveBuy : liveSell;
         if(!qualifies)
           {
            if(idx >= 0)
              {
               DeleteMarker(idx);
               if(side > 0)
                  liveBuy = -1;
               else
                  liveSell = -1;
              }
            continue;
           }
         if(idx < 0)
           {
            idx = ArraySize(markers);
            ArrayResize(markers, idx + 1);
            markers[idx].id = nextId++;
            markers[idx].bar = bar;
            markers[idx].side = side;
            if(side > 0)
               liveBuy = idx;
            else
               liveSell = idx;
           }
         markers[idx].price = bestPx;
         markers[idx].volume = shown;
         Draw(idx, time);
        }
     }

   void              DeleteMarker(const int i)
     {
      string id = IntegerToString(markers[i].id);
      ObjectDelete(0, PFX + "c" + id);
      ObjectDelete(0, PFX + "r" + id);
      ObjectDelete(0, PFX + "t" + id);
      ArrayRemove(markers, i, 1);
      if(liveBuy > i)
         liveBuy--;
      if(liveSell > i)
         liveSell--;
     }

   int               SizeOf(const double v)
     {
      // square-root scale against the biggest marker on the chart
      double vmax = v;
      for(int i = 0; i < ArraySize(markers); i++)
         vmax = MathMax(vmax, markers[i].volume);
      double f = MathSqrt(MathMax(0.0, MathMin(1.0, v / MathMax(vmax, 1e-9))));
      return (int)MathRound(InpMinSize + (InpMaxSize - InpMinSize) * f);
     }

   void              Draw(const int i, const datetime &time[])
     {
      string id = IntegerToString(markers[i].id);
      color base = markers[i].side > 0 ? InpBullColor : InpBearColor;
      datetime t = time[markers[i].bar] + PeriodSeconds() / 2;
      int size = SizeOf(markers[i].volume);
      IdcText(PFX + "c" + id, t, markers[i].price, IdcGlyph(IDC_WD_CIRCLE), IdcBlend(base, InpFillOpacity / 100.0), size, "Wingdings", ANCHOR_CENTER, false);
      if(InpRing)
         IdcText(PFX + "r" + id, t, markers[i].price, IdcGlyph(IDC_WD_RING), IdcBlend(base, 0.85), size, "Wingdings", ANCHOR_CENTER, false);
      IdcText(PFX + "t" + id, t, markers[i].price, IdcVolText(markers[i].volume, true), InpTextColor, InpTextSize, "Arial Bold", ANCHOR_CENTER, false);
     }

   //--- once the history is loaded: circles are sized against the biggest marker
   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      double vmax = 0;
      for(int i = 0; i < ArraySize(markers); i++)
         vmax = MathMax(vmax, markers[i].volume);
      if(vmax <= 0)
         return;
      for(int i = 0; i < ArraySize(markers); i++)
        {
         double f = MathSqrt(MathMax(0.0, MathMin(1.0, markers[i].volume / vmax)));
         int size = (int)MathRound(InpMinSize + (InpMaxSize - InpMinSize) * f);
         string id = IntegerToString(markers[i].id);
         if(ObjectFind(0, PFX + "c" + id) < 0)
            Draw(i, time);
         ObjectSetInteger(0, PFX + "c" + id, OBJPROP_FONTSIZE, size);
         if(InpRing)
            ObjectSetInteger(0, PFX + "r" + id, OBJPROP_FONTSIZE, size);
        }
     }
  };

CHeavyAbsorption g_ind;

int OnInit()
  {
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.rowSize = IdcRowSize(_Symbol, InpRowPoints);
   g_ind.tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(g_ind.tick <= 0)
      g_ind.tick = _Point;
   g_ind.fp.rowSize = g_ind.rowSize;
   g_ind.nextId = 0;
   g_ind.liveBuy = -1;
   g_ind.liveSell = -1;
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
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC Heavy Absorption: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
