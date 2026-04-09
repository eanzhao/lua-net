local mt = {
  __le = function(a, b)
    return a.rank <= b
  end
}

local x = setmetatable({ rank = 5 }, mt)

if x <= 5 then
  return "lei"
end

return "gt"
