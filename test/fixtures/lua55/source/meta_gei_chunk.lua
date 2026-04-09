local mt = {
  __le = function(a, b)
    return a <= b.rank
  end
}

local x = setmetatable({ rank = 5 }, mt)

if 5 <= x then
  return "gei"
end

return "lt"
