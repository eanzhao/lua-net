local chars = { string.byte("Aπ", 1, 3) }
local found_start, found_end = string.find("hello 123", "%d+")
local plain_start, plain_end = string.find("banana", "na", 3, true)
local word, digits = string.match("abc 42", "(%a+)%s+(%d+)")
local pos1, pos2 = string.match("aabb", "()bb()")
local balanced = string.match("xx(a(b)c)yy", "%b()")
local replaced, replaced_count = string.gsub("cat 42 dog", "(%a+)", "[%1]")
local iterator = string.gmatch("x=10,y=20", "(%a)=(%d+)")
local key1, value1 = iterator()
local key2, value2 = iterator()
local packed = string.pack("<I2I2", 513, 1027)
local formatted = string.format("%s|%d|%.2f|%q", "lua", 7, 2.5, "a\nb")
local packed1, packed2, packed3, packed4 = string.byte(packed, 1, 4)

return
  chars[1], chars[2], chars[3],
  string.char(65, 66, 67),
  string.rep("ab", 3, "-"),
  string.reverse("abc"),
  string.sub("Aπ", 2, -1),
  found_start, found_end,
  plain_start, plain_end,
  word, digits,
  pos1, pos2,
  balanced,
  replaced, replaced_count,
  key1, value1, key2, value2,
  formatted,
  string.packsize("<I2I2"),
  packed1, packed2, packed3, packed4,
  string.unpack("<I2I2", packed)
