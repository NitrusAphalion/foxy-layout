# foxy-layout
Layout manager for NinjaTrader 8 loosely based on https://golden-layout.com/

![](https://s6.gifyu.com/images/S6uSU.gif)

Demo video: https://www.veed.io/view/81899ae5-d34c-494a-b3e1-2e22ba6c2409?panel=

Features:
 - Drag and drop to organize windows
 - Rotate through possible layouts of the same drag position on a timer (Just drop when you see the one you want)
 - Resize multiple windows together
 - Unique control center position PER WORKSPACE
 - Under Tools -> Foxy Layout you can customize:
     - Whether the snapping behavior applies automatically or only when you drag and drop while holding a modifier key
     - Whether to bring all windows to front when one is clicked
     - The background/border for the drag/drop zone
     - The background/border for the 'illegal operation' zone

Note: There are still some quirks for this running on multi-monitor and 4k setups.

You may need to update the NinjaTrader dll references to build -> build process should stick the dll in your bin/Custom folder -> Just launch NT8 and enjoy.


