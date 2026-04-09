local function pack(...)
  return ...
end

local t = {pack(4, 5, 6)}
return t[1], t[2], t[3]
