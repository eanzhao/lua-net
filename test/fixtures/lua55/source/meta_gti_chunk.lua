local mt = {
  __lt = function(a, b)
    return a < b.rank
  end
}

local x = setmetatable({ rank = 5 }, mt)

if 2 < x then
  return "gti"
end

return "le"
