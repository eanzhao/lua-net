local named = setmetatable({}, { __name = "vec" })
local custom = setmetatable({}, {
  __tostring = function()
    return "custom"
  end
})

return tostring(3.0), tostring(named), tostring(custom), tostring(function() end)
