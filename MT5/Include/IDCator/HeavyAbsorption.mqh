//+------------------------------------------------------------------+
//| IDC Heavy Absorption - module: inputs and logic                  |
//| Used alone by IDC_Heavy_Absorption.mq5                           |
//| and with the other tools by IDC_OrderFlow.mq5.                   |
//|                                                                  |
//| Marks exceptionally heavy aggressive volume at one price inside  |
//| a bar: green circle = buyers, red = sellers. Threshold = mean +  |
//| k standard deviations of the heaviest price per bar and side     |
//| over the lookback window. The number is the aggressive volume at |
//| that price and keeps counting up on the forming bar.             |
//| With exchange trades a single big print can be used instead.     |
//+------------------------------------------------------------------+
#ifndef IDC_HEAVY_ABSORPTION_MQH
#define IDC_HEAVY_ABSORPTION_MQH

#include <IDCator/IDCatorCore.mqh>

enum ENUM_IDC_HA_ALERT
  {
   IDC_HA_ALERT_OFF   = 0, // Off
   IDC_HA_ALERT_LIVE  = 1, // Live (when a circle appears)
   IDC_HA_ALERT_CLOSE = 2  // On bar close
  };

enum ENUM_IDC_HA_UNIT
  {
   IDC_HA_PRICE = 0, // Volume at one price (footprint cell)
   IDC_HA_PRINT = 1  // Single print (exchange trades only)
  };

input group "Heavy Absorption: Detection"
input ENUM_IDC_HA_UNIT InpHaUnit          = IDC_HA_PRICE;    // What counts as heavy
input int              InpHaLookback      = 100;             // Lookback bars
input double           InpHaK             = 2.0;             // Std dev multiplier
input int              InpHaMinSamples    = 40;              // Min samples before marking
input double           InpHaMinVolume     = 0;               // Min volume filter (hide smaller)
input group "Heavy Absorption: Display"
input color            InpHaBullColor     = C'38,166,91';    // Bull circle
input color            InpHaBearColor     = C'235,64,52';    // Bear circle
input int              InpHaFillOpacity   = 32;              // Fill opacity %
input bool             InpHaRing          = true;            // Ring
input color            InpHaTextColor     = clrWhite;        // Text color
input int              InpHaTextSize      = 8;               // Text size
input int              InpHaMinSize       = 14;              // Min circle size
input int              InpHaMaxSize       = 34;              // Max circle size
input int              InpHaMaxMarkers    = 300;             // Max markers on chart
input group "Heavy Absorption: Alerts"
input ENUM_IDC_HA_ALERT InpHaAlert        = IDC_HA_ALERT_OFF; // Alert

#define HA_PFX "IDCha_"

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
   //--- live alerts sent: bar and sides (1 buy, 2 sell)
   int               alertBar, alertSides;

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, HA_PFX);
      ArrayResize(markers, 0);
      nextId = 0;
      fp.Clear();
      stats.Init(InpHaLookback);
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
         Evaluate(bar, time, InpHaAlert == IDC_HA_ALERT_LIVE);   // realtime: the marker follows the forming bar trade by trade
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      Evaluate(bar, time, false);
      double bestBuy = 0, bestSell = 0;
      HeaviestRows(bestBuy, bestSell);
      if(InpHaUnit == IDC_HA_PRINT && UsesTrades())
        {
         bestBuy = printBuy;
         bestSell = printSell;
        }
      if(bestBuy > 0)
         stats.Add(bestBuy);
      if(bestSell > 0)
         stats.Add(bestSell);
      stats.CloseBar();
      if(live && InpHaAlert == IDC_HA_ALERT_CLOSE)
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
      while(ArraySize(markers) > InpHaMaxMarkers)
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

   //--- creates, moves or removes the forming bar's markers; alertNew: alert once per side and bar when a marker appears
   void              Evaluate(const int bar, const datetime &time[], const bool alertNew)
     {
      bool ready = stats.Count() >= InpHaMinSamples;
      double mean = stats.Mean();
      double thr = stats.Threshold(InpHaK);
      for(int side = 1; side >= -1; side -= 2)
        {
         double best = 0, bestPx = 0, shown = 0;
         if(InpHaUnit == IDC_HA_PRINT && UsesTrades())
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
         if(InpHaUnit != IDC_HA_PRINT || !UsesTrades())
            shown = best;
         else
           {
            // the number shows the same-side volume at the print's price, as in the videos
            int row = (int)(fp.RowOf(bestPx) - fp.lowRow);
            if(row >= 0 && row < fp.Rows())
               shown = side > 0 ? fp.buy[row] : fp.sell[row];
           }
         bool qualifies = ready && best >= thr && best > mean && shown >= InpHaMinVolume && best > 0;
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
            int bit = side > 0 ? 1 : 2;
            if(alertBar != bar)
              {
               alertBar = bar;
               alertSides = 0;
              }
            if(alertNew && (alertSides & bit) == 0)
              {
               alertSides |= bit;
               Alert(_Symbol + (side > 0 ? " heavy BUY " : " heavy SELL ") + IdcVolText(shown, true) + " @ " + DoubleToString(bestPx, _Digits));
              }
           }
         markers[idx].price = bestPx;
         markers[idx].volume = shown;
         Draw(idx, time);
        }
     }

   void              DeleteMarker(const int i)
     {
      string id = IntegerToString(markers[i].id);
      ObjectDelete(0, HA_PFX + "c" + id);
      ObjectDelete(0, HA_PFX + "r" + id);
      ObjectDelete(0, HA_PFX + "t" + id);
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
      return (int)MathRound(InpHaMinSize + (InpHaMaxSize - InpHaMinSize) * f);
     }

   void              Draw(const int i, const datetime &time[])
     {
      string id = IntegerToString(markers[i].id);
      color base = markers[i].side > 0 ? InpHaBullColor : InpHaBearColor;
      datetime t = time[markers[i].bar] + PeriodSeconds() / 2;
      int size = SizeOf(markers[i].volume);
      IdcText(HA_PFX + "c" + id, t, markers[i].price, IdcGlyph(IDC_WD_CIRCLE), IdcBlend(base, InpHaFillOpacity / 100.0), size, "Wingdings", ANCHOR_CENTER, false);
      if(InpHaRing)
         IdcText(HA_PFX + "r" + id, t, markers[i].price, IdcGlyph(IDC_WD_RING), IdcBlend(base, 0.85), size, "Wingdings", ANCHOR_CENTER, false);
      IdcText(HA_PFX + "t" + id, t, markers[i].price, IdcVolText(markers[i].volume, true), InpHaTextColor, InpHaTextSize, "Arial Bold", ANCHOR_CENTER, false);
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
         int size = (int)MathRound(InpHaMinSize + (InpHaMaxSize - InpHaMinSize) * f);
         string id = IntegerToString(markers[i].id);
         if(ObjectFind(0, HA_PFX + "c" + id) < 0)
            Draw(i, time);
         ObjectSetInteger(0, HA_PFX + "c" + id, OBJPROP_FONTSIZE, size);
         if(InpHaRing)
            ObjectSetInteger(0, HA_PFX + "r" + id, OBJPROP_FONTSIZE, size);
        }
     }

   //--- symbol tick and the initial row size (rowPoints 0: one tick until SetRow)
   void              Init(const int rowPoints)
     {
      tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      if(tick <= 0)
         tick = _Point;
      SetRow(IdcRowSize(_Symbol, rowPoints));
      nextId = 0;
      liveBuy = -1;
      liveSell = -1;
      alertBar = -1;
      alertSides = 0;
     }

   //--- footprint row size (automatic: IdcAutoRow from the chart's bars)
   void              SetRow(const double row)
     {
      rowSize = row;
      fp.rowSize = row;
     }
  };

#endif
