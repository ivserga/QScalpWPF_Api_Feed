// =======================================================================
//    TabOther.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

using System;

namespace QScalp.Windows
{
  partial class ConfigWindow
  {
    // **********************************************************************

    void InitOther()
    {
      // REST API
      apiBaseUrl.Text = cfg.u.ApiBaseUrl;
      apiKey.Password = cfg.u.ApiKey;
      pollInterval.Value = cfg.u.PollInterval;
      apiDataDate.Text = cfg.u.ApiDataDate;
      
      // Скорость воспроизведения
      int speedIndex = 0;
      switch(cfg.u.PlaybackSpeed)
      {
        case 1: speedIndex = 0; break;
        case 2: speedIndex = 1; break;
        case 5: speedIndex = 2; break;
        case 10: speedIndex = 3; break;
        case 50: speedIndex = 4; break;
        case 100: speedIndex = 5; break;
        case 200: speedIndex = 6; break;
        case 300: speedIndex = 7; break;
        default: speedIndex = 0; break;
      }
      playbackSpeed.SelectedIndex = speedIndex;

      // DDE (устаревший)
      ddeServerName.Text = cfg.u.DdeServerName;

      enableQuikLog.IsChecked = cfg.u.EnableQuikLog;
      acceptAllTrades.IsChecked = cfg.u.AcceptAllTrades;

      emulatorDelayMin.Value = cfg.u.EmulatorDelayMin;
      emulatorDelayMin.ValueChanged += new EventHandler(emulatorDelayMin_ValueChanged);

      emulatorDelayMax.Value = cfg.u.EmulatorDelayMax;
      emulatorDelayMax.ValueChanged += new EventHandler(emulatorDelayMax_ValueChanged);

      emulatorLimit.Value = cfg.u.EmulatorLimit;

      fontFamily.Text = cfg.u.FontFamily;
      fontSize.Value = cfg.u.FontSize;
    }

    // **********************************************************************

    void emulatorDelayMin_ValueChanged(object sender, EventArgs e)
    {
      if(emulatorDelayMax.Value < emulatorDelayMin.Value)
        emulatorDelayMax.Value = emulatorDelayMin.Value;
    }

    // **********************************************************************

    void emulatorDelayMax_ValueChanged(object sender, EventArgs e)
    {
      if(emulatorDelayMin.Value > emulatorDelayMax.Value)
        emulatorDelayMin.Value = emulatorDelayMax.Value;
    }

    // **********************************************************************

    void ApplyOther()
    {
      // REST API
      cfg.u.ApiBaseUrl = apiBaseUrl.Text;
      cfg.u.ApiKey = apiKey.Password;
      cfg.u.PollInterval = (int)pollInterval.Value;
      cfg.u.ApiDataDate = apiDataDate.Text.Trim();
      
      // Скорость воспроизведения
      if(playbackSpeed.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
      {
        cfg.u.PlaybackSpeed = Convert.ToInt32(item.Tag);
      }

      // DDE (устаревший)
      cfg.u.DdeServerName = ddeServerName.Text;

      cfg.u.EnableQuikLog = enableQuikLog.IsChecked == true;
      cfg.u.AcceptAllTrades = acceptAllTrades.IsChecked == true;

      cfg.u.EmulatorDelayMin = (int)emulatorDelayMin.Value;
      cfg.u.EmulatorDelayMax = (int)emulatorDelayMax.Value;
      cfg.u.EmulatorLimit = (int)emulatorLimit.Value;

      cfg.u.FontFamily = fontFamily.Text;
      cfg.u.FontSize = fontSize.Value;
    }

    // **********************************************************************
  }
}
