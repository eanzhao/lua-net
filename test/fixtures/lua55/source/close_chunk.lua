local function outer()
  local f

  do
    local x = 40
    f = function()
      return x
    end
  end

  local x = 99
  return f, x
end

local f, x = outer()
return f(), x
