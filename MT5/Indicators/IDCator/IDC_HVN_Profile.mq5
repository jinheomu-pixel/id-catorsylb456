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

#include <IDCator/HvnProfile.mqh>

CHvnProfile g_ind;

int OnInit()
  {
   if(PeriodSeconds(InpHvnPeriod) <= PeriodSeconds())
      Print("IDC HVN Profile: choose a profile timeframe higher than the chart timeframe");
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.Init(0);
   EventSetTimer(1);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   ObjectsDeleteAll(0, HVN_PFX);
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
   // automatic row size from the chart's bars, set at every (re)start
   if(prev_calculated == 0 && InpHvnBasePoints <= 0)
      g_ind.SetRow(IdcAutoRow(high, low, rates_total, g_ind.tick));
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC HVN Profile: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
