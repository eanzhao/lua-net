return
  tonumber(" 0x10 "),
  tonumber("0x1.8p1"),
  tonumber("3.5"),
  tonumber("ff", 16),
  tonumber("-10", 2),
  tonumber("19", 8) == nil and "nil" or "bad",
  tonumber(true) == nil and "nil" or "bad"
