local mt = {
  __lt = function(a, b)
    return a.rank < b.rank
  end
}

local x = setmetatable({ rank = 2 }, mt)
local y = setmetatable({ rank = 5 }, mt)

if x < y then
  return "lt"
end

return "ge"
