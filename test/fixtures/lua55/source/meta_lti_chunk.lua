local mt = {
  __lt = function(a, b)
    return a.rank < b
  end
}

local x = setmetatable({ rank = 2 }, mt)

if x < 5 then
  return "lti"
end

return "ge"
