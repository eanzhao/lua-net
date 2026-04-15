local single = { only = 42 }
local nextKey, nextValue = next(single)
local nextDone = next(single, nextKey) == nil and 1 or 0

local regular = { a = 10, b = 20, [3] = 30 }
local pairCount = 0
local pairSum = 0

for _, value in pairs(regular) do
  pairCount = pairCount + 1
  pairSum = pairSum + value
end

local custom = setmetatable({}, {
  __pairs = function(self)
    return function(_, control)
      if control == nil then
        return "tag", 99
      end

      return nil
    end, self, nil
  end
})

local metaKey = "missing"
local metaValue = 0

for key, value in pairs(custom) do
  metaKey = key
  metaValue = value
end

local ipairsCount = 0
local ipairsSum = 0

for index, value in ipairs({ 10, 20, nil, 40 }) do
  ipairsCount = ipairsCount + 1
  ipairsSum = ipairsSum + index + value
end

return nextKey, nextValue, nextDone, pairCount, pairSum, metaKey, metaValue, ipairsCount, ipairsSum
