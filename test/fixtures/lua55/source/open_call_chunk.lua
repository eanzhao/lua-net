local function pack(...)
  return ...
end

local function count(a, b, c)
  return a, b, c
end

local function forward(...)
  return count(pack(...))
end

return forward(1, 2, 3)
