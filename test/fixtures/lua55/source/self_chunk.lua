local t = {}

function t:join(suffix)
  return self.name .. suffix
end

t.name = "lua"

return t:join("-net")
