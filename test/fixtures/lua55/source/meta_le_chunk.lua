local mt = {
  __le = function(a, b)
    return a.rank <= b.rank
  end
}

local x = setmetatable({ rank = 5 }, mt)
local y = setmetatable({ rank = 5 }, mt)

if x <= y then
  return "le"
end

return "gt"
