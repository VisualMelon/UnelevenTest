/*
 * Borrowed from SharpDX MiniCube example, and heavily added to and modified
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using sdw = SharpDX.DirectWrite;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

// so we can C&P HLSL
using dword = System.UInt32;
using float3 = SharpDX.Vector3;
using float4 = SharpDX.Vector4;
using matrix = SharpDX.Matrix;

namespace UN11
{
	public class UN11
	{
		public static class Utils
		{
			public static void copy<T>(int si, T[] src, int di, T[] dst, int count)
			{
				for (int i = 0; i < count; i++)
				{
					dst[i + di] = src[i + si];
				}
			}
			
			public static unsafe void copy(byte* src, byte* dst, int count)
			{
				for (int i = 0; i < count; i++)
				{
					*dst = *src;
					dst++;
					src++;
				}
			}
		}
		
		public class BloominEckException : Exception
		{
			public BloominEckException(string msg) : base(msg)
			{
				// joy
			}
		}
		
		public static void texAlignViewProj(ref Matrix vp)
		{
			Matrix translate, scale;
			
			Matrix.Scaling(0.5f, -0.5f, 1.0f, out scale);
			Matrix.Multiply(ref vp, ref scale, out vp);
			Matrix.Translation(0.5f, 0.5f, 0.0f, out translate);
			Matrix.Multiply(ref vp, ref translate, out vp);
		}
		
		public class TextureCollection : NamedCollection<NamedTexture>
		{
		}
		
		public class TextureView
		{
			public ShaderResourceView texShaderView;
			
			public TextureView(ShaderResourceView texShaderViewN)
			{
				texShaderView = texShaderViewN;
			}
			
			public void applyShaderResource(DeviceContext context, int slot)
			{
				context.PixelShader.SetShaderResource(slot, texShaderView);
			}
			
			public void applyShaderResource(DeviceContext context, TextureSlot slot)
			{
				context.PixelShader.SetShaderResource((int)slot, texShaderView);
			}
		}
		
		public class NamedTexture : TextureView, Named
		{
			public string name {get; private set;}
			
			// some of these might not exist, so don't assume they do
			public Texture2D tex;
			
//			public NamedTexture(string name) : base(name)
//			{
//				// joy
//			}
			
			public NamedTexture(string nameN, Texture2D texN, ShaderResourceView texShaderViewN) : base(texShaderViewN)
			{
				tex = texN;
				name = nameN;
			}
		}
		
		public class NamedRenderTexture : NamedTexture
		{
			public RenderTargetView texRenderView;
			
			public NamedRenderTexture(string name, Texture2D texN, ShaderResourceView texShaderViewN, RenderTargetView texRenderViewN) : base(name, texN, texShaderViewN)
			{
				texRenderView = texRenderViewN;
			}
		}
		
		public class RenderViewPair
		{
			public RenderTargetView renderView;
			public DepthStencilView stencilView;
			public Color clearColour;
			
			public RenderViewPair()
			{
				// joy
			}
			
			public RenderViewPair(RenderTargetView renderViewN, DepthStencilView stencilViewN)
			{
				renderView = renderViewN;
				stencilView = stencilViewN;
			}
			
			public void apply(DeviceContext context, bool clearDepth, bool clearColor)
			{
				context.OutputMerger.SetRenderTargets(stencilView, renderView);
				if (clearDepth)
					context.ClearDepthStencilView(stencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
				if (clearColor)
					context.ClearRenderTargetView(renderView, clearColour);
			}
		}
		
		public static void createStencilBuffer(Device device, int texWidth, int texHeight, out Texture2D stencilTex, out DepthStencilView stencilView)
		{
			Texture2DDescription stencilDesc = new Texture2DDescription();
			stencilDesc.ArraySize = 1;
			stencilDesc.MipLevels = 1;
			stencilDesc.Width = texWidth;
			stencilDesc.Height = texHeight;
			stencilDesc.Format = Format.D32_Float_S8X24_UInt;
			stencilDesc.SampleDescription = new SampleDescription(1, 0);
			stencilDesc.Usage = ResourceUsage.Default;
			stencilDesc.BindFlags = BindFlags.DepthStencil;
			stencilDesc.CpuAccessFlags = CpuAccessFlags.None;
			stencilTex = new Texture2D(device, stencilDesc);
			
			DepthStencilViewDescription stencilViewDesc = new DepthStencilViewDescription();
			stencilViewDesc.Format = Format.D32_Float_S8X24_UInt;
			stencilViewDesc.Dimension = DepthStencilViewDimension.Texture2D;
			stencilViewDesc.Texture2D.MipSlice = 0;
			stencilView = new DepthStencilView(device, stencilTex, stencilViewDesc);
		}
		
		public static NamedRenderTexture createNamedRenderTexture(Device device, string name, int texWidth, int texHeight, Format format)
		{
			Texture2D tex;
			ShaderResourceView texShaderView;
			RenderTargetView texRenderView;
			
			createTextureSet(device, texWidth, texHeight, format, out tex, out texRenderView, out texShaderView);
			return new NamedRenderTexture(name, tex, texShaderView, texRenderView);
		}
		
		public static NamedTexture createRenderNamedTexture(Device device, string name, int texWidth, int texHeight, Format format, out RenderTargetView texRenderView)
		{
			Texture2D tex;
			ShaderResourceView texShaderView;
			
			createTextureSet(device, texWidth, texHeight, format, out tex, out texRenderView, out texShaderView);
			return new NamedTexture(name, tex, texShaderView);
		}
		
		public static void createTextureSet(Device device, int texWidth, int texHeight, Format format, out Texture2D targetTex, out RenderTargetView targetRenderView, out ShaderResourceView targetShaderView)
		{
			Texture2DDescription targetDesc = new Texture2DDescription();
			targetDesc.ArraySize = 1;
			targetDesc.MipLevels = 1;
			targetDesc.Width = texWidth;
			targetDesc.Height = texHeight;
			targetDesc.Format = format;
			targetDesc.SampleDescription = new SampleDescription(1, 0);
			targetDesc.Usage = ResourceUsage.Default;
			targetDesc.BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource;
			targetDesc.CpuAccessFlags = CpuAccessFlags.None;
			
			targetTex = new Texture2D(device, targetDesc);
			
			RenderTargetViewDescription targetRenderViewDesc = new RenderTargetViewDescription();
			targetRenderViewDesc.Format = format;
			targetRenderViewDesc.Dimension = RenderTargetViewDimension.Texture2D;
			targetRenderViewDesc.Texture2D.MipSlice = 0;
			targetRenderView = new RenderTargetView(device, targetTex, targetRenderViewDesc);
			
			ShaderResourceViewDescription targetShaderViewDesc = new ShaderResourceViewDescription();
			targetShaderViewDesc.Format = format;
			targetShaderViewDesc.Dimension = ShaderResourceViewDimension.Texture2D;
			targetShaderViewDesc.Texture2D.MostDetailedMip = 0;
			targetShaderViewDesc.Texture2D.MipLevels = 1;
			targetShaderView = new ShaderResourceView(device, targetTex, targetShaderViewDesc);
		}
		
		public class Timing
		{
			public class Span : ANamed
			{
				Timing timing;
				
				private long cumulative;
				private long startTime;
				
				public Span(String name, Timing timingN) : base(name)
				{
					timing = timingN;
					reset();
				}
				
				/// <summary>
				/// Zero assumulated time
				/// </summary>
				public void reset()
				{
					cumulative = 0;
					startTime = -1;
				}
				
				/// <summary>
				/// Add ellapsed to accumulated, and restart
				/// </summary>
				public void accumulate()
				{
					cumulative += ellapsed;
					start();
				}
				
				/// <summary>
				/// Stop timing, but preserve previously accumulated
				/// </summary>
				public void cut()
				{
					startTime = -1;
				}
				
				public void start()
				{
					startTime = timing.curTime;
				}
				
				public void stop()
				{
					if (startTime != -1)
					{
						cumulative += timing.curTime - startTime;
						startTime = -1;
					}
				}
				
				public long accumulated
				{
					get
					{
						return cumulative;
					}
				}
				
				public long ellapsed
				{
					get
					{
						if (startTime != -1)
							return  timing.curTime - startTime;
						else
							return 0;
					}
				}
				
				public long total
				{
					get
					{
						return accumulated + ellapsed;
					}
				}
			}
			
			private Stopwatch sw;
			private NamedCollection<Span> spans;
			
			public Timing()
			{
				sw = new Stopwatch();
				spans = new NamedCollection<Span>();
			}
			
			public static float toSeconds(long ticks)
			{
				return (float)((double)ticks / (double)System.Diagnostics.Stopwatch.Frequency);
			}
			
			public Span newSpan(string name)
			{
				Span res = new Span(name, this);
				spans.Add(res);
				return res;
			}
			
			public void remSpan(string name)
			{
				spans.Remove(name);
			}
			
			public void remSpan(Span s)
			{
				spans.Remove(s);
			}
			
			public Span this[string name]
			{
				get
				{
					return spans[name];
				}
				set
				{
					spans[name] = value;
				}
			}
			
			public long curTime
			{
				get
				{
					return sw.ElapsedTicks;
				}
			}
			
			// these effectivly effect all of the subordinates, probably should never be used
			public void stop()
			{
				sw.Stop();
			}
			
			public void start()
			{
				sw.Start();
			}
			
			public void reset()
			{
				sw.Reset();
			}
		}
		
		// enums
		public enum VertexType
		{
			VertexPC,
			VertexPCT,
			VertexOver
		}
		
		public enum AlphaMode
		{
			None,
			Nice,
			Add
		}
		
		public enum LightingMode
		{
			None,
			Full
		}
		
		public enum ViewMode
		{
			Ortho,
			Persp
		}
		
		public enum LightType
		{
			Ortho = 0,
			Persp = 1,
			Point = 2
		}
		
		public enum TextureSlot : int
		{
			tex = 0,
			tex0 = 1,
			tex1 = 2,
			tex2 = 3,
			tex3 = 4,
			sideTex = 5,
			targetTex = 5,
		}
		
		public enum SamplerSlot : int
		{
			linearWrap = 0,
			pointWrap = 1,
			linearBorder = 2,
			pointBorder = 3
		}
		
		// vertex decs
		public static InputElement[] vertexLayoutArr(VertexType vertexType)
		{
			switch (vertexType)
			{
				case VertexType.VertexPC:
					return VertexPC.layoutArr;
				case VertexType.VertexPCT:
					return VertexPCT.layoutArr;
				case VertexType.VertexOver:
					return VertexOver.layoutArr;
				default:
					return null;
			}
		}
		
		[StructLayout(LayoutKind.Explicit)]
		public struct VertexPC
		{
			public static readonly InputElement[] layoutArr;
			public static readonly int size;
			
			static VertexPC()
			{
				layoutArr = new[]
				{
					new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
					new InputElement("NORMAL", 0, Format.R32G32B32A32_Float, 16, 0),
					new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 32, 0),
					new InputElement("TEXCOORD", 0, Format.R32_Float, 48, 0),
				};
				
				size = Utilities.SizeOf<VertexPC>();
			}
			
			[FieldOffset(0)] public Vector3 pos3;
			[FieldOffset(0)] public Vector4 pos4;
			[FieldOffset(0)] public float x;
			[FieldOffset(4)] public float y;
			[FieldOffset(8)] public float z;
			[FieldOffset(12)] public float w;
			
			[FieldOffset(16)] public Vector3 nrm3;
			[FieldOffset(16)] public Vector4 nrm4;
			[FieldOffset(16)] public float nx;
			[FieldOffset(20)] public float ny;
			[FieldOffset(24)] public float nz;
			[FieldOffset(28)] public float nw;
			
			[FieldOffset(32)] public Vector3 col3;
			[FieldOffset(32)] public Vector4 col4;
			[FieldOffset(32)] public float r;
			[FieldOffset(36)] public float g;
			[FieldOffset(40)] public float b;
			[FieldOffset(44)] public float a;
			
			[FieldOffset(48)] public float tti;
			
			public VertexPC(Vector4 posN, Vector4 colN) : this()
			{
				pos4 = posN;
				
				nx = 0.0F;
				ny = 0.0F;
				nz = 0.0F;
				nw = 0.0F;
				
				col4 = colN;
				
				tti = -1;
			}
			
			public VertexPC(Vector3 posN, Vector3 colN, float ttiN) : this()
			{
				pos3 = posN;
				w = 1.0F;
				
				nx = 0.0F;
				ny = 0.0F;
				nz = 0.0F;
				nw = 0.0F;
				
				a = 1.0F;
				col3 = colN;
				
				tti = ttiN;
			}
			
			public VertexPC(Vector3 posN, float rN, float gN, float bN, float ttiN) : this()
			{
				pos3 = posN;
				w = 1.0F;
				
				nx = 0.0F;
				ny = 0.0F;
				nz = 0.0F;
				nw = 0.0F;
				
				a = 1.0F;
				r = rN;
				g = gN;
				b = bN;
				
				tti = ttiN;
			}
			
			public VertexPC(float xN, float yN, float zN, Vector3 colN, float ttiN) : this()
			{
				x = xN;
				y = yN;
				z = zN;
				w = 1.0F;
				
				nx = 0.0F;
				ny = 0.0F;
				nz = 0.0F;
				nw = 0.0F;
				
				a = 1.0F;
				col3 = colN;
				
				tti = ttiN;
			}
			
			public VertexPC(float xN, float yN, float zN, float rN, float gN, float bN, float ttiN) : this()
			{
				x = xN;
				y = yN;
				z = zN;
				w = 1.0F;
				
				nx = 0.0F;
				ny = 0.0F;
				nz = 0.0F;
				nw = 0.0F;
				
				a = 1.0F;
				r = rN;
				g = gN;
				b = bN;
				
				tti = ttiN;
			}
			
			public VertexPC(float xN, float yN, float zN, float nxN, float nyN, float nzN, float rN, float gN, float bN, float ttiN) : this()
			{
				x = xN;
				y = yN;
				z = zN;
				w = 1.0F;
				
				nx = nxN;
				ny = nyN;
				nz = nzN;
				nw = 0.0F;
				
				a = 1.0F;
				r = rN;
				g = gN;
				b = bN;
				
				tti = ttiN;
			}
		}
		
		
		
		[StructLayout(LayoutKind.Explicit)]
		public struct VertexPCT
		{
			public static readonly InputElement[] layoutArr;
			public static readonly int size;
			
			static VertexPCT()
			{
				layoutArr = new[]
				{
					new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
					new InputElement("NORMAL", 0, Format.R32G32B32A32_Float, 16, 0),
					new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 32, 0),
					new InputElement("TEXCOORD", 0, Format.R32G32_Float, 48, 0),
					new InputElement("TEXCOORD", 1, Format.R32_Float, 56, 0),
				};
				
				size = Utilities.SizeOf<VertexPCT>();
			}
			
			[FieldOffset(0)] public Vector3 pos3;
			[FieldOffset(0)] public Vector4 pos4;
			[FieldOffset(0)] public float x;
			[FieldOffset(4)] public float y;
			[FieldOffset(8)] public float z;
			[FieldOffset(12)] public float w;
			
			[FieldOffset(16)] public Vector3 nrm3;
			[FieldOffset(16)] public Vector4 nrm4;
			[FieldOffset(16)] public float nx;
			[FieldOffset(20)] public float ny;
			[FieldOffset(24)] public float nz;
			[FieldOffset(28)] public float nw;
			
			[FieldOffset(32)] public Vector3 col3;
			[FieldOffset(32)] public Vector4 col4;
			[FieldOffset(32)] public float r;
			[FieldOffset(36)] public float g;
			[FieldOffset(40)] public float b;
			[FieldOffset(44)] public float a;

			[FieldOffset(48)] public Vector2 tex2;
			[FieldOffset(48)] public float tu;
			[FieldOffset(52)] public float tv;
			
			[FieldOffset(56)] public float tti;
			
			public VertexPCT(VertexPC vpcN, float tuN, float tvN) : this()
			{
				pos4 = vpcN.pos4;
				nrm4 = vpcN.nrm4;
				col4 = vpcN.col4;
				tti = vpcN.tti;
				
				tu = tuN;
				tv = tvN;
			}
		}
		
		[StructLayout(LayoutKind.Explicit)]
		public struct VertexOver
		{
			public static readonly InputElement[] layoutArr;
			public static readonly int size;
			
			static VertexOver()
			{
				layoutArr = new[]
				{
					new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
					new InputElement("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
				};
				
				size = Utilities.SizeOf<VertexOver>();
			}
			
			[FieldOffset(0)] public Vector3 pos3;
			[FieldOffset(0)] public Vector4 pos4;
			[FieldOffset(0)] public float x;
			[FieldOffset(4)] public float y;
			[FieldOffset(8)] public float z;
			[FieldOffset(12)] public float w;
			
			[FieldOffset(16)] public Vector2 pos2;
			[FieldOffset(16)] public float tu;
			[FieldOffset(20)] public float tv;
			
			public VertexOver(float xN, float yN, float zN, float tuN, float tvN) : this()
			{
				x = xN;
				y = yN;
				z = zN;
				w = 1.0f;
				
				tu = tuN;
				tv = tvN;
			}
		}
		
		public class ShaderBytecodeDesc
		{
			public string fileName {get; private set;}
			public string shaderName {get; private set;}
			public string profile {get; private set;}
			
			#region Equals and GetHashCode implementation
			public override bool Equals(object obj)
			{
				ShaderBytecodeDesc other = obj as ShaderBytecodeDesc;
				if (other == null)
					return false;
				return this.fileName == other.fileName && this.shaderName == other.shaderName && this.profile == other.profile;
			}
			
			public override int GetHashCode()
			{
				int hashCode = 0;
				unchecked {
					if (fileName != null)
						hashCode += 1000000007 * fileName.GetHashCode();
					if (shaderName != null)
						hashCode += 1000000009 * shaderName.GetHashCode();
					hashCode += 1000000021 * profile.GetHashCode();
				}
				return hashCode;
			}
			
			public static bool operator ==(ShaderBytecodeDesc lhs, ShaderBytecodeDesc rhs)
			{
				if (ReferenceEquals(lhs, rhs))
					return true;
				if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
					return false;
				return lhs.Equals(rhs);
			}
			
			public static bool operator !=(ShaderBytecodeDesc lhs, ShaderBytecodeDesc rhs)
			{
				return !(lhs == rhs);
			}
			#endregion
			
			public ShaderBytecodeDesc(string fileNameN, string shaderNameN, string profileN)
			{
				fileName = fileNameN;
				shaderName = shaderNameN;
				profile = profileN;
			}
		}
		
		public class ShaderBytecodeCollection
		{
			private Dictionary<ShaderBytecodeDesc, ShaderBytecode> bytecodes = new Dictionary<ShaderBytecodeDesc, ShaderBytecode>();
			
			public ShaderBytecodeDesc loadShaderBytecode(string fileName, string shaderName, string profile)
			{
				ShaderBytecodeDesc temp = new ShaderBytecodeDesc(fileName, shaderName, profile);
				loadShaderBytecode(temp);
				return temp;
			}
			
			public ShaderBytecode loadShaderBytecode(ShaderBytecodeDesc desc)
			{
				ShaderBytecode temp;
				if (!bytecodes.TryGetValue(desc, out temp))
				{
					temp = ShaderBytecode.CompileFromFile(desc.fileName, desc.shaderName, desc.profile);
					bytecodes[desc] = temp;
				}
				return temp;
			}
			
			public void disposeAll()
			{
				foreach (ShaderBytecode sb in bytecodes.Values)
				{
					sb.Dispose();
				}
			}
		}
		
		public interface Named
		{
			string name {get;}
		}
		
		public abstract class ANamed : Named
		{
			public string name {get; private set;}
			
			public ANamed(string nameN)
			{
				name = nameN;
			}
		}
		
		// unordered, fast
		public class NamedCollection<T> : System.Collections.IEnumerable where T : class, Named
		{
			private Dictionary<string, T> items = new Dictionary<string, T>();
			
			public bool TryGetValue(string name, out T res)
			{
				return items.TryGetValue(name, out res);
			}
			
			public T Get(string name)
			{
				T res;
				if (TryGetValue(name, out res))
					return res;
				else
					return null;
			}
			
			public void Add(T item)
			{
				items.Add(item.name, item);
			}
			
			public void Set(T item)
			{
				items[item.name] = item;
			}
			
			public void Remove(string name)
			{
				items.Remove(name);
			}
			
			public void Remove(T item)
			{
				items.Remove(item.name);
			}
			
			public bool ContainsKey(string name)
			{
				return items.ContainsKey(name);
			}
			
			public T this[string name]
			{
				get
				{
					return Get(name);
				}
				set
				{
					items[name] = value;
				}
			}
			
			public int Count()
			{
				return items.Count;
			}
			
			public System.Collections.IEnumerator GetEnumerator()
			{
				return items.Values.GetEnumerator();
			}
		}
		
		// ordered, slow
		public class NamedList<T> : System.Collections.IEnumerable where T : class, Named
		{
			private List<T> items = new List<T>();
			
			public bool TryGetValue(string name, out T res)
			{
				foreach (T item in items)
				{
					if (item.name == name)
					{
						res = item;
						return true;
					}
				}
				
				res = null;
				return false;
			}
			
			public T Get(string name)
			{
				T res;
				if (TryGetValue(name, out res))
					return res;
				else
					return null;
			}
			
			public bool TryGetIndex(string name, out int idx)
			{
				for (int i = 0; i < items.Count; i++)
				{
					if (items[i].name == name)
					{
						idx = i;
						return true;
					}
				}
				
				idx = -1;
				return false;
			}
			
			public int GetIndex(string name)
			{
				int idx;
				if (TryGetIndex(name, out idx))
					return idx;
				else
					return -1;
			}
			
			public void Add(T item)
			{
				items.Add(item);
			}
			
			public void Set(int idx, T item)
			{
				int oidx;
				if (TryGetIndex(item.name, out oidx))
			    {
					items.RemoveAt(oidx);
					items.Insert(idx, item);
			    }
				else
				{
					items.Insert(idx, item);
				}
			}
			
			public void Insert(int idx, T item)
			{
				items.Insert(idx, item);
			}
			
			public void Remove(string name)
			{
				for (int i = items.Count - 1; i >= 0; i--)
				{
					if (items[i].name == name)
					{
						items.RemoveAt(i);
						return;
					}
				}
			}
			
			public void Remove(T item)
			{
				for (int i = items.Count - 1; i >= 0; i--)
				{
					if (items[i].name == item.name) // stick with behaviour of NamedCollection
					{
						items.RemoveAt(i);
						return;
					}
				}
			}
			
			public bool ContainsKey(string name)
			{
				int idx;
				return TryGetIndex(name, out idx);
			}
			
			public bool ToFront(string name)
			{
				for (int i = items.Count - 1; i >= 0; i--)
				{
					T item = items[i];
					if (item.name == name) // stick with behaviour of NamedCollection
					{
						items.RemoveAt(i);
						items.Insert(0, item);
						return true;
					}
				}
				
				return false;
			}
			
			public bool ToBack(string name)
			{
				for (int i = 0; i < items.Count; i++)
				{
					T item = items[i];
					if (item.name == name) // stick with behaviour of NamedCollection
					{
						items.RemoveAt(i);
						items.Add(item);
						return true;
					}
				}
				
				return false;
			}
			
			public T this[string name]
			{
				get
				{
					return Get(name);
				}
				set
				{
					for (int i = items.Count - 1; i >= 0; i--)
					{
						if (items[i].name == name) // stick with behaviour of NamedCollection
						{
							items[i] = value;
						}
					}
				}
			}
			
			public int Count()
			{
				return items.Count;
			}
			
			public System.Collections.IEnumerator GetEnumerator()
			{
				return items.GetEnumerator();
			}
			
			public IEnumerable<T> EnumerateForwards()
			{
				for (int i = 0; i < items.Count; i++)
				{
					yield return items[i];
				}
			}
			
			public IEnumerable<T> EnumerateBackwards()
			{
				for (int i = items.Count - 1; i > 0; i--)
				{
					yield return items[i];
				}
			}
		}
		
		public class TechniqueCollection : NamedCollection<Technique>
		{
		}
		
		public class Technique : ANamed
		{
			public List<Pass> passes = new List<Pass>();
			
			public Technique(string name) : base(name)
			{
				// joy
			}
		}
		
		public class Pass
		{
			public VertexType vertexType {get; private set;}
			private InputLayout layout;
			
			public ShaderBytecodeDesc vshadeDesc {get; private set;}
			private VertexShader vshade;
			private ShaderBytecode vshadeBytecode;
			
			public ShaderBytecodeDesc pshadeDesc {get; private set;}
			private PixelShader pshade;
			private ShaderBytecode pshadeBytecode;
			
			public Pass(Device device, VertexType vertexTypeN, ShaderBytecodeDesc vshadeDescN, ShaderBytecodeDesc pshadeDescN, ShaderBytecodeCollection bytecodes)
			{
				vshadeDesc = vshadeDescN;
				pshadeDesc = pshadeDescN;
				
				vshadeBytecode = bytecodes.loadShaderBytecode(vshadeDescN);
				pshadeBytecode = bytecodes.loadShaderBytecode(pshadeDescN);
				
				vshade = new VertexShader(device, vshadeBytecode);
				pshade = new PixelShader(device, pshadeBytecode);
				
				vertexType = vertexTypeN;
				InputElement[] larr = vertexLayoutArr(vertexTypeN);
				layout = new InputLayout(device, ShaderSignature.GetInputSignature(vshadeBytecode), larr); // has to be done after vshade creation etc. etc.
			}
			
			public void apply(DeviceContext context)
			{
				context.InputAssembler.InputLayout = layout;

				context.VertexShader.Set(vshade);
				context.PixelShader.Set(pshade);
			}
		}
		
		public class AppliableViewport
		{
			public Viewport vp;
			
			public AppliableViewport(int x, int y, int w, int h, float near, float far)
			{
				vp = new Viewport(x, y, w, h, near, far);
			}
			
			public void apply(DeviceContext context)
			{
				context.Rasterizer.SetViewport(vp);
			}
		}
		
		public class AppliableBlendState : BlendState
		{
			public AppliableBlendState(Device device, BlendStateDescription desc) : base(device, desc)
			{
				// joy
			}
			
			public void apply(DeviceContext context)
			{
				context.OutputMerger.BlendState = this;
			}
		}
		
		// states
		public class BlendStates
		{
			public AppliableBlendState none;
			
			public AppliableBlendState addOneOne;
			public AppliableBlendState addSrcInvSrc;
			public AppliableBlendState addOneInvSrc;
			public AppliableBlendState addSrcOne;
			public AppliableBlendState addZeroOne;
			
			public BlendStates(Device device)
			{
				BlendStateDescription noneDesc = new BlendStateDescription();
				noneDesc.RenderTarget[0] = new RenderTargetBlendDescription(false, /*don't care -->*/ BlendOption.One, BlendOption.One, BlendOperation.Add, BlendOption.Zero, BlendOption.One, BlendOperation.Add /*<-- don't care*/, ColorWriteMaskFlags.All);
				none = new AppliableBlendState(device, noneDesc);
				
				
				BlendStateDescription addOneOneDesc = new BlendStateDescription();
				addOneOneDesc.RenderTarget[0] = new RenderTargetBlendDescription(true, BlendOption.One, BlendOption.One, BlendOperation.Add, BlendOption.Zero, BlendOption.One, BlendOperation.Add, ColorWriteMaskFlags.All);
				addOneOne = new AppliableBlendState(device, addOneOneDesc);

				BlendStateDescription addSrcInvSrcDesc = new BlendStateDescription();
				addSrcInvSrcDesc.RenderTarget[0] = new RenderTargetBlendDescription(true, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOperation.Add, BlendOption.One, BlendOption.Zero, BlendOperation.Add, ColorWriteMaskFlags.All);
				addSrcInvSrc = new AppliableBlendState(device, addSrcInvSrcDesc);

				BlendStateDescription addOneInvSrcDesc = new BlendStateDescription();
				addOneInvSrcDesc.RenderTarget[0] = new RenderTargetBlendDescription(true, BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, BlendOption.One, BlendOption.Zero, BlendOperation.Add, ColorWriteMaskFlags.All);
				addOneInvSrc = new AppliableBlendState(device, addOneInvSrcDesc);
				
				BlendStateDescription addSrcOneDesc = new BlendStateDescription();
				addSrcOneDesc.RenderTarget[0] = new RenderTargetBlendDescription(true, BlendOption.SourceAlpha, BlendOption.One, BlendOperation.Add, BlendOption.One, BlendOption.Zero, BlendOperation.Add, ColorWriteMaskFlags.All);
				addSrcOne = new AppliableBlendState(device, addSrcOneDesc);

				BlendStateDescription addZeroOneDesc = new BlendStateDescription();
				addZeroOneDesc.RenderTarget[0] = new RenderTargetBlendDescription(true, BlendOption.Zero, BlendOption.One, BlendOperation.Add, BlendOption.Zero, BlendOption.One, BlendOperation.Add, ColorWriteMaskFlags.All);
				addZeroOne = new AppliableBlendState(device, addZeroOneDesc);

			}
		}
		
		public class AppliableDepthStencilState : DepthStencilState
		{
			public AppliableDepthStencilState(Device device, DepthStencilStateDescription desc) : base(device, desc)
			{
				// joy
			}
			
			public void apply(DeviceContext context)
			{
				context.OutputMerger.DepthStencilState = this;
			}
		}
		
		public class DepthStencilStates
		{
			public AppliableDepthStencilState zNone;
			public AppliableDepthStencilState zReadWrite;
			public AppliableDepthStencilState zRead;
			
			public DepthStencilStates(Device device)
			{
				DepthStencilStateDescription zNoneDesc = new DepthStencilStateDescription();
				zNoneDesc.IsDepthEnabled = false;
				zNoneDesc.IsStencilEnabled = false;
				zNoneDesc.DepthWriteMask = DepthWriteMask.Zero;
				zNoneDesc.DepthComparison = Comparison.LessEqual; // meh
				zNone = new AppliableDepthStencilState(device, zNoneDesc);
				
				DepthStencilStateDescription zReadWriteDesc = new DepthStencilStateDescription();
				zReadWriteDesc.IsDepthEnabled = true;
				zReadWriteDesc.IsStencilEnabled = false;
				zReadWriteDesc.DepthWriteMask = DepthWriteMask.All;
				zReadWriteDesc.DepthComparison = Comparison.LessEqual;
				zReadWrite = new AppliableDepthStencilState(device, zReadWriteDesc);
				
				DepthStencilStateDescription zReadDesc = new DepthStencilStateDescription();
				zReadDesc.IsDepthEnabled = true;
				zReadDesc.IsStencilEnabled = false;
				zReadDesc.DepthWriteMask = DepthWriteMask.Zero;
				zReadDesc.DepthComparison = Comparison.LessEqual;
				zRead = new AppliableDepthStencilState(device, zReadDesc);
			}
		}
		
		public class AppliableRasterizerState : RasterizerState
		{
			public AppliableRasterizerState(Device device, RasterizerStateDescription desc) : base(device, desc)
			{
				// joy
			}
			
			public void apply(DeviceContext context)
			{
				context.Rasterizer.State = this;
			}
		}
		
		public class RasterizerStates
		{
			public AppliableRasterizerState ccBackcull;
			public AppliableRasterizerState ccFrontcull;
			public AppliableRasterizerState noBackcull;
			public AppliableRasterizerState wireframe;
			
			public RasterizerStates(Device device)
			{
				RasterizerStateDescription ccBackcullDesc = new RasterizerStateDescription();
				ccBackcullDesc.CullMode = CullMode.Back;
				ccBackcullDesc.IsFrontCounterClockwise = true;
				ccBackcullDesc.IsScissorEnabled = false;
				ccBackcullDesc.IsMultisampleEnabled = false;
				ccBackcullDesc.IsDepthClipEnabled = true;
				ccBackcullDesc.FillMode = FillMode.Solid;
				ccBackcull = new AppliableRasterizerState(device, ccBackcullDesc);
				
				RasterizerStateDescription ccFrontcullDesc = new RasterizerStateDescription();
				ccFrontcullDesc.CullMode = CullMode.Front;
				ccFrontcullDesc.IsFrontCounterClockwise = true;
				ccFrontcullDesc.IsScissorEnabled = false;
				ccFrontcullDesc.IsMultisampleEnabled = false;
				ccFrontcullDesc.IsDepthClipEnabled = true;
				ccFrontcullDesc.FillMode = FillMode.Solid;
				ccFrontcull = new AppliableRasterizerState(device, ccFrontcullDesc);
				
				RasterizerStateDescription noBackcullDesc = new RasterizerStateDescription();
				noBackcullDesc.CullMode = CullMode.None;
				noBackcullDesc.IsFrontCounterClockwise = true;
				noBackcullDesc.IsScissorEnabled = false;
				noBackcullDesc.IsMultisampleEnabled = false;
				noBackcullDesc.IsDepthClipEnabled = true;
				noBackcullDesc.FillMode = FillMode.Solid;
				noBackcull = new AppliableRasterizerState(device, noBackcullDesc);
				
				RasterizerStateDescription wireframeDesc = new RasterizerStateDescription();
				wireframeDesc.CullMode = CullMode.None;
				wireframeDesc.IsFrontCounterClockwise = true;
				wireframeDesc.IsScissorEnabled = false;
				wireframeDesc.IsMultisampleEnabled = false;
				wireframeDesc.IsDepthClipEnabled = true;
				wireframeDesc.FillMode = FillMode.Wireframe;
				wireframe = new AppliableRasterizerState(device, wireframeDesc);
			}
		}
		
		public class AppliableSamplerState : SamplerState
		{
			public AppliableSamplerState(Device device, SamplerStateDescription desc) : base(device, desc)
			{
				// joy
			}
			
			public void Apply(DeviceContext context, int slot)
			{
				context.PixelShader.SetSampler(slot, this);
			}
		}
		
		public class SamplerStates
		{
			public AppliableSamplerState linearWrap;
			public AppliableSamplerState pointWrap;
			public AppliableSamplerState linearBorder;
			public AppliableSamplerState pointBorder;
			
			public SamplerStates(Device device)
			{
				SamplerStateDescription linearWrapDesc = new SamplerStateDescription();
				linearWrapDesc.AddressU = TextureAddressMode.Wrap;
				linearWrapDesc.AddressV = TextureAddressMode.Wrap;
				linearWrapDesc.AddressW = TextureAddressMode.Wrap;
				linearWrapDesc.Filter = Filter.MinMagMipLinear;
				linearWrapDesc.BorderColor = Color4.Black;
				linearWrapDesc.ComparisonFunction = Comparison.Never;
				linearWrap = new AppliableSamplerState(device, linearWrapDesc);
				
				SamplerStateDescription pointWrapDesc = new SamplerStateDescription();
				pointWrapDesc.AddressU = TextureAddressMode.Wrap;
				pointWrapDesc.AddressV = TextureAddressMode.Wrap;
				pointWrapDesc.AddressW = TextureAddressMode.Wrap;
				pointWrapDesc.Filter = Filter.MinMagMipPoint;
				pointWrapDesc.BorderColor = Color4.Black;
				pointWrapDesc.ComparisonFunction = Comparison.Never;
				pointWrap = new AppliableSamplerState(device, pointWrapDesc);
				
				SamplerStateDescription linearBorderDesc = new SamplerStateDescription();
				linearBorderDesc.AddressU = TextureAddressMode.Border;
				linearBorderDesc.AddressV = TextureAddressMode.Border;
				linearBorderDesc.AddressW = TextureAddressMode.Border;
				linearBorderDesc.Filter = Filter.MinMagMipLinear;
				linearBorderDesc.BorderColor = Color4.Black;
				linearBorderDesc.ComparisonFunction = Comparison.Never;
				linearBorder = new AppliableSamplerState(device, linearBorderDesc);
				
				SamplerStateDescription pointBorderDesc = new SamplerStateDescription();
				pointBorderDesc.AddressU = TextureAddressMode.Border;
				pointBorderDesc.AddressV = TextureAddressMode.Border;
				pointBorderDesc.AddressW = TextureAddressMode.Border;
				pointBorderDesc.Filter = Filter.MinMagMipPoint;
				pointBorderDesc.BorderColor = Color4.Black;
				pointBorderDesc.ComparisonFunction = Comparison.Never;
				pointBorder = new AppliableSamplerState(device, pointBorderDesc);
			}
			
		}
		
		// the slide sorts this out
		[StructLayout(LayoutKind.Sequential)]
		public struct EyeCData
		{
			public const int defaultSlot = 0;
			
			public matrix viewProj; // 0, 16
			public float4 eyePos;
			public float4 eyeDir;
			public float farDepth;
			public float invFarDepth;
			public float pad2;
			public float pad3;
		}
		
		// defined per-light
		[StructLayout(LayoutKind.Sequential)]
		public struct LightCData
		{
			public const int defaultSlot = 1; // shared with LightMapCData
			
			public matrix lightViewProj;
			public float4 lightPos;
			public float4 lightDir;
			public float4 lightAmbient;
			public float4 lightColMod;
			public float lightCoof;
			public float lightDepth;
			public float lightDodge;
			public float lightType;
		}
		
		// the (mappable) light sorts this out
		[StructLayout(LayoutKind.Sequential)]
		public struct LightMapCData
		{
			public const int defaultSlot = 1; // shared with LightCData
			
			public matrix lightViewProj;
			public float4 lightPos;
			public float4 lightDir;
			public float4 lightAmbient;
			public float4 lightColMod;
			public float lightCoof;
			public float lightDepth;
			public float lightDodge;
			public float lightType;
		}
		
		// models/segments work this out
		[StructLayout(LayoutKind.Explicit, Size=TransCData.maxTransMats * sizeof(float) * 16)]
		public struct TransCData
		{
			public const int defaultSlot = 2;
			public const int maxTransMats = 30;
			
			[FieldOffsetAttribute(0)]
			public Matrix mat0;
		}
		
		// models/segments work this out
		[StructLayout(LayoutKind.Explicit, Size=SpriteCData.maxSprite4s * sizeof(float) * 4)]
		public struct SpriteCData
		{
			public const int defaultSlot = 2;
			public const int maxSprite4s = 120;
			
			[FieldOffsetAttribute(0)]
			public Vector4 vec0;
		}
		
		[StructLayout(LayoutKind.Sequential)]
		public struct OverCData
		{
			public const int defaultSlot = 3;
			
			public float4 texData;
		}
		
		[StructLayout(LayoutKind.Sequential)]
		public struct TargetCData
		{
			public const int defaultSlot = 3;
			
			public matrix targetVPMat;
			public float4 texData;
		}
		
		[StructLayout(LayoutKind.Sequential)]
		public struct SectionCData
		{
			public const int defaultSlot = 4;
			
			public float4 colMod;
		}
		
		public interface IConstBuffering
		{
			void update(DeviceContext context);
			
			void applyVStage(DeviceContext context);
			
			void applyPStage(DeviceContext context);
		}
		
		public class ConstBuffer<T> : IConstBuffering, IDisposable where T : struct
		{
			public T data;
			public Buffer buffer {get; private set;}
			public BufferDescription desc {get; private set;}
			public int slot;
			
			/// <summary>/
			/// Create a const buffer with explicit buffer description
			/// </summary>
			/// <param name="device"></param>
			/// <param name="data"></param>
			/// <param name="desc"></param>
			public ConstBuffer(Device device, T dataN, BufferDescription descN, int slotN)
			{
				data = dataN;
				desc = descN;
				slot = slotN;
				buffer = new Buffer(device, desc);
			}
			
			/// <summary>/
			/// Create a const buffer with explicit buffer description
			/// </summary>
			/// <param name="device"></param>
			/// <param name="desc"></param>
			public ConstBuffer(Device device, BufferDescription descN, int slotN)
			{
				data = new T();
				desc = descN;
				slot = slotN;
				buffer = new Buffer(device, desc);
			}
			
			/// <summary>
			/// Create a const buffer which will create a buffer description based on the size of T
			/// </summary>
			/// <param name="device"></param>
			/// <param name="data"></param>
			/// <param name="desc"></param>
			public ConstBuffer(Device device, T dataN, int slotN)
			{
				data = dataN;
				slot = slotN;
				desc = new BufferDescription(Utilities.SizeOf<T>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
				buffer = new Buffer(device, desc);
			}
			
			/// <summary>
			/// Create a const buffer which will create a buffer description based on the size of T
			/// </summary>
			/// <param name="device"></param>
			/// <param name="desc"></param>
			public ConstBuffer(Device device, int slotN)
			{
				data = new T();
				slot = slotN;
				desc = new BufferDescription(Utilities.SizeOf<T>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
				buffer = new Buffer(device, desc);
			}
			
			public void update(DeviceContext context)
			{
				context.UpdateSubresource(ref data, buffer);
			}
			
			public void applyVStage(DeviceContext context)
			{
				context.VertexShader.SetConstantBuffer(slot, buffer);
			}
			
			public void applyVStage(DeviceContext context, int altSlot)
			{
				context.VertexShader.SetConstantBuffer(altSlot, buffer);
			}
			
			public void applyPStage(DeviceContext context)
			{
				context.PixelShader.SetConstantBuffer(slot, buffer);
			}
			
			public void applyPStage(DeviceContext context, int altSlot)
			{
				context.PixelShader.SetConstantBuffer(altSlot, buffer);
			}
			
			public void Dispose()
			{
				buffer.Dispose();
			}
		}
		
		public class Overness
		{
			public int texWidth;
			public int texHeight;
			
			private AppliableViewport vp;
			private ConstBuffer<EyeCData> eyeBuffer;
			private VertexOver[] overVerts;
			private ConstBuffer<OverCData> overBuffer;
			private Buffer overVBuff;
			private VertexBufferBinding overVBuffBinding;
			
			public Overness(int texWidthN, int texHeightN)
			{
				texWidth = texWidthN;
				texHeight = texHeightN;
				
				vp = new AppliableViewport(0, 0, texWidth, texHeight, 0.0f, 1.0f);
			}
			
			private void updateEyeCData()
			{
				Matrix idMat = Matrix.Identity;
				matrix.Transpose(ref idMat, out eyeBuffer.data.viewProj);
				eyeBuffer.data.farDepth = 0.0f;
				eyeBuffer.data.invFarDepth = 1.0f;
			}
			
			public void build(Device device)
			{
				eyeBuffer = new ConstBuffer<EyeCData>(device, EyeCData.defaultSlot);
				updateEyeCData();
				
				if (overVBuff != null)
					overVBuff.Dispose();
				if (overBuffer != null)
					overBuffer.Dispose();
				
				overVerts = new VertexOver[4];
				overVerts[0] = new VertexOver(-1, -1, 0, 0, 1);
				overVerts[1] = new VertexOver(-1, 1, 0, 0, 0);
				overVerts[2] = new VertexOver(1, -1, 0, 1, 1);
				overVerts[3] = new VertexOver(1, 1, 0, 1, 0);
				
				Vector4 texData = new Vector4(0.5f / texWidth, 0.5f / texHeight, 1.0f / texWidth, 1.0f / texHeight);
				
				// need an over buffer
				overBuffer = new ConstBuffer<OverCData>(device, OverCData.defaultSlot);
				overBuffer.data.texData = texData;
				
				
				// might not need to do this any more -- looks like we don't! ty DX10 team
				// TODO: work out what else we don't need (i.e. all the stuff above with stupid divisions and stuff? - maybe leave it for DX9/OGL shaders? not like we need anything /more/ and float4 is a tidy sum (this is good idea))
				/*for (int i = 0; i < 4; i++) // do ahead of shader
				{
					overVerts[i].tu += texData.X;
					overVerts[i].tv += texData.Y;
				}*/
				
				overVBuff = Buffer.Create(device, BindFlags.VertexBuffer, overVerts);
				
				overVBuffBinding = new VertexBufferBinding(overVBuff, VertexOver.size, 0);
			}
			
			public void apply(DeviceContext context)
			{
				overBuffer.update(context);
				overBuffer.applyPStage(context);
				
				eyeBuffer.update(context);
				eyeBuffer.applyVStage(context);
				
				context.InputAssembler.SetVertexBuffers(0, overVBuffBinding);
				context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip; // this needs to be put in other places, because I probably assume it's TriangleList everwhere
				
				vp.apply(context);
			}
			
			public void drawOver(DeviceContext context)
			{
				context.Draw(4, 0);
			}
		}
		
		public class Texness
		{
			public bool useTex;
			public TextureView tex;
			public bool useTex0;
			public TextureView tex0;
			public bool useTex1;
			public TextureView tex1;
			public bool useTex2;
			public TextureView tex2;
			public bool useTex3;
			public TextureView tex3;
			
			public void applyTextures(DeviceContext context)
			{
				// something like this, I think
				if (useTex)
					tex.applyShaderResource(context, UN11.TextureSlot.tex);
				if (useTex0)
					tex0.applyShaderResource(context, UN11.TextureSlot.tex0);
				if (useTex1)
					tex1.applyShaderResource(context, UN11.TextureSlot.tex1);
				if (useTex2)
					tex2.applyShaderResource(context, UN11.TextureSlot.tex2);
				if (useTex3)
					tex3.applyShaderResource(context, UN11.TextureSlot.tex3);
			}
			
			public Texness()
			{
			}
			
			public Texness(Texness gin)
			{
				useTex = gin.useTex;
				tex = gin.tex;
				useTex0 = gin.useTex0;
				tex0 = gin.tex0;
				useTex1 = gin.useTex1;
				tex1 = gin.tex1;
				useTex2 = gin.useTex2;
				tex2 = gin.tex2;
				useTex3 = gin.useTex3;
				tex3 = gin.tex3;
			}
		}
		
		public class Prettyness
		{
			public Vector4 colMod;
			
			public Technique tech;
			public Technique litTech;
			public Technique lightTech;
			public Technique decalTech;
			public Technique dynamicDecalTech;
			public Technique overTech;
			
			public VertexType vertexType;
			public AlphaMode alphaMode;
			public LightingMode lightingMode;
			
			public Prettyness()
			{
			}
			
			public Prettyness(Prettyness gin)
			{
				colMod = gin.colMod;
				
				tech = gin.tech; // plain pass tech
				litTech = gin.litTech;
				lightTech = gin.lightTech;
				decalTech = gin.decalTech;
				dynamicDecalTech = gin.dynamicDecalTech;
				overTech = gin.overTech;
				
				vertexType = gin.vertexType;
				alphaMode = gin.alphaMode;
				lightingMode = gin.lightingMode;
			}
			
			// TODO: work out if this still needs to exist
			public void setAlpha(DeviceContext context, DrawData ddat, AlphaMode alphaMode)
			{
				switch (alphaMode)
				{
					case AlphaMode.None:
						ddat.pddat.uneleven.blendStates.none.apply(context); // or something like this
						break;
				}
			}
		}
		
		public enum SceneType : int
		{
			Colour,
			Light,
			Free0,
			
			Length = 2 // don't forget to change me if you want more than 2
		}
		
		public class SectionPrettyness
		{
			public Prettyness prettyness;
			public Texness texness;
			
			public ConstBuffer<SectionCData> sectionBuffer;
			
			public SectionPrettyness(Device device)
			{
				texness = new Texness();
				prettyness = new Prettyness();
				
				createSectionBuffer(device);
			}
			
			public SectionPrettyness(Device device, SectionPrettyness gin)
			{
				texness = new Texness(gin.texness);
				prettyness = new Prettyness(gin.prettyness);
				
				createSectionBuffer(device);
			}
			
			private void createSectionBuffer(Device device)
			{
				sectionBuffer = new ConstBuffer<SectionCData>(device, SectionCData.defaultSlot);
			}
			
			public void update()
			{
				sectionBuffer.data.colMod = prettyness.colMod;
			}
			
			public void apply(DeviceContext context)
			{
				sectionBuffer.update(context);
				
				sectionBuffer.applyVStage(context);
				sectionBuffer.applyPStage(context);
				
				texness.applyTextures(context);
			}
		}
		
		public class Section : Named
		{
			public string name {get; private set;}
			
			public SectionPrettyness[] prettynessess;
			
			public int batchCopies;
			public int indexOffset;
			public int triCount; // formerly vLen
			
			public int indexCount;
			
			public bool drawDecals;
			public bool acceptDecals;
			public bool drawDynamicDecals;
			
			public bool sectionEnabled; // whether it should draw or not
			
			public Matrix[] mats; // TODO: what is this
			
			public bool curDrawCull;
			
			public Section(Device device, string nameN) : base()
			{
				name = nameN;
				
				prettynessess = new UN11.SectionPrettyness[(int)SceneType.Length];
				for (int i = 0; i < prettynessess.Length; i++)
					prettynessess[i] = new SectionPrettyness(device);
			}
			
			public Section(Device device, Section gin)
			{
				name = gin.name;
				
				prettynessess = new UN11.SectionPrettyness[(int)SceneType.Length];
				for (int i = 0; i < prettynessess.Length; i++)
					prettynessess[i] = new SectionPrettyness(device, gin.prettynessess[i]);
				
				batchCopies = gin.batchCopies;
				indexOffset = gin.indexOffset;
				triCount = gin.triCount;
				
				indexCount = gin.indexCount;
				
				drawDecals = gin.drawDecals;
				acceptDecals = gin.acceptDecals;
				drawDynamicDecals = gin.drawDynamicDecals;
			
				sectionEnabled = gin.sectionEnabled;
				
				mats = gin.mats; // TODO: what is this
			}
			
			private void drawPrims(DeviceContext context, int batchCount)
			{
				context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList; // might want to find a better way of doing this, but we used to pass this to DrawIndexedPrimative, so it can't be too slow
				context.DrawIndexed(triCount * batchCount * 3, indexOffset * batchCopies, 0);
			}
			
			public void update()
			{
				foreach (SectionPrettyness sp in prettynessess)
					sp.update();
			}
			
			public void setVertexType(VertexType vertexType)
			{
				foreach  (SectionPrettyness sp in prettynessess)
				{
					sp.prettyness.vertexType = vertexType;
				}
			}
			
			// will lag behind drawDraw, don't worry about it (has a bit of draw as well)
			public void drawMany(DeviceContext context, DrawData ddat, ManyModelDrawData mmddat, int secIndex)
			{
				if (sectionEnabled == false)
					return;
				
				SectionPrettyness prettyness = prettynessess[(int)ddat.sceneType];
				
				ddat.targetRenderViewPair.apply(context, false, false);
				ddat.pddat.uneleven.depthStencilStates.zReadWrite.apply(context);
				ddat.pddat.uneleven.rasterizerStates.ccFrontcull.apply(context);
					
				ddat.eyeBuffer.applyVStage(context);
				ddat.eyeBuffer.update(context);
				
				if (ddat.sceneType == SceneType.Light)
				{
					// this should probably actually do the checks
					ddat.lightMapBuffer.applyVStage(context); // ??
					ddat.lightMapBuffer.applyPStage(context); // ??
					ddat.lightMapBuffer.update(context);
				}
				
				if (!mmddat.useOwnSections)
				{
					prettyness.apply(context);
				}
				
				ddat.pddat.uneleven.blendStates.none.apply(context);
				
				//setmats (TODO: needs another stupid unsafe constant buffer)
				
				// plain pass
				if (ddat.sceneType == SceneType.Light)
					prettyness.prettyness.lightTech.passes[(int)ddat.lightMapBuffer.data.lightType].apply(context);
				else if (prettyness.prettyness.tech != null) // TODO: make this explicit
					prettyness.prettyness.tech.passes[0].apply(context);
				else
					goto noPlainPass;
				
				foreach (Model m in mmddat.models)
				{
					if (mmddat.useOwnSections)
					{
						Section msec = m.sections[secIndex];
						if (msec.curDrawCull)
							continue;
					
						msec.prettynessess[(int)ddat.sceneType].apply(context);
					}
					
					m.transArrBuffer.update(context);
					m.transArrBuffer.apply(context);
					
					drawPrims(context, 1);
				}
				
			noPlainPass:
				
				if (ddat.sceneType != SceneType.Light && prettyness.prettyness.lightingMode == LightingMode.Full && prettyness.prettyness.litTech != null)
				{
					ddat.pddat.uneleven.blendStates.addOneOne.apply(context);
					
					foreach (Light l in ddat.lights)
					{
						if (!l.lightEnabled)
							continue;
						
						l.lightBuffer.update(context);
						l.lightBuffer.applyVStage(context);
						l.lightBuffer.applyPStage(context);
						
						prettyness.prettyness.litTech.passes[(int)l.lightType].apply(context);
						
						foreach (Model m in mmddat.models)
						{
							if (mmddat.useOwnSections)
							{
								Section msec = m.sections[secIndex];
								if (msec.curDrawCull)
									continue;
							
								msec.prettynessess[(int)ddat.sceneType].apply(context);
							}
							
							m.transArrBuffer.update(context);
							m.transArrBuffer.apply(context);
							
							drawPrims(context, 1);
						}
					}
				}
			}
			
			public void draw(DeviceContext context, DrawData ddat)
			{
				SectionPrettyness prettyness = prettynessess[(int)ddat.sceneType];
				
				if (sectionEnabled == false)
					return;
				
				if (ddat.sceneType == SceneType.Light || prettyness.prettyness.alphaMode == AlphaMode.None)
				{
					ddat.targetRenderViewPair.apply(context, false, false);
					ddat.pddat.uneleven.depthStencilStates.zReadWrite.apply(context);
					ddat.pddat.uneleven.rasterizerStates.ccFrontcull.apply(context);
					drawDraw(context, ddat);
				}
				else
				{
					// work out rasterizer states for fillmode etc.
					drawToSide(context, ddat);
					drawSideOver(context, ddat);
				}
			}
			
			public void drawSideOver(DeviceContext context, DrawData ddat)
			{
				SectionPrettyness prettyness = prettynessess[(int)ddat.sceneType];
				
				// disable clip
				ddat.targetRenderViewPair.apply(context, false, false);
				ddat.sideTex.applyShaderResource(context, (int)TextureSlot.sideTex);
				ddat.pddat.uneleven.depthStencilStates.zNone.apply(context);
				
				prettyness.apply(context);
				ddat.overness.apply(context);
				foreach (Pass p in prettyness.prettyness.overTech.passes)
				{
					p.apply(context);
					ddat.overness.drawOver(context);
				}
			}
			
			public void drawToSide(DeviceContext context, DrawData ddat)
			{
				SectionPrettyness prettyness = prettynessess[(int)ddat.sceneType];
				
				ddat.sideRenderViewPair.apply(context, true, true);
				ddat.targetTex.applyShaderResource(context, (int)TextureSlot.targetTex);
				ddat.pddat.uneleven.depthStencilStates.zReadWrite.apply(context);
				
				ddat.targetBuffer.applyPStage(context);
				
				drawDraw(context, ddat);
			}
			
			// TODO: consider re-working the how technique/sceneType thing so that draw draw
			// takes a type of scene or something, and just swaps out the type of tech
			// (e.g. SceneType.Lit -> use litTech (inc. tech (position 0) like it used to)
			// and SceneType.Ligt -> use lightTech, etc. etc.)
			// Would need then a pair for each thing (i.e. main model, decals, dyn decals)
			public void drawDraw(DeviceContext context, DrawData ddat)
			{
				SectionPrettyness prettyness = prettynessess[(int)ddat.sceneType];
				
				ddat.eyeBuffer.applyVStage(context);
				ddat.eyeBuffer.update(context);
				
				prettyness.apply(context);
				
				ddat.pddat.uneleven.blendStates.none.apply(context);
				
				//setmats (TODO: needs another stupid unsafe constant buffer)
				
				// plain pass
				if (ddat.sceneType == SceneType.Light)
					prettyness.prettyness.lightTech.passes[(int)ddat.lightMapBuffer.data.lightType].apply(context);
				else if (prettyness.prettyness.tech != null) // TODO: make this explicit
					prettyness.prettyness.tech.passes[0].apply(context);
				else
					goto noPlainPass;
				
				drawPrims(context, 1);
				
			noPlainPass:
				
				if (ddat.sceneType != SceneType.Light && prettyness.prettyness.lightingMode == LightingMode.Full && prettyness.prettyness.litTech != null)
				{
					ddat.pddat.uneleven.blendStates.addOneOne.apply(context);
					
					foreach (Light l in ddat.lights)
					{
						if (!l.lightEnabled)
							continue;
						
						l.lightBuffer.update(context);
						l.lightBuffer.applyVStage(context);
						l.lightBuffer.applyPStage(context);
						
						prettyness.prettyness.litTech.passes[(int)l.lightType].apply(context);
						
						drawPrims(context, 1);
					}
				}
			}
		}
		
		public struct OffRot
		{
			public Vector3 offset;
			public Vector3 rotation;
			public Matrix offsetMatrix;
			public Matrix rotationMatrix;
			public Matrix offsetMatrixInv;
			public Matrix rotationMatrixInv;
			//public Matrix localTrans;
			//public Matrix localTransInv;
			
			public OffRot(Vector3 offN, Vector3 rotN) : this()
			{
				offset = offN;
				rotation = rotN;
			}
			
			public Matrix transMat
			{
				get
				{
					Matrix mat = Matrix.Identity;
					Matrix.Multiply(ref rotationMatrix, ref mat, out mat);
					Matrix.Multiply(ref offsetMatrix, ref mat, out mat);
					return mat;
				}
			}
			
			public void trans(ref Matrix mat)
			{
				Matrix.Multiply(ref offsetMatrix, ref mat, out mat);
				Matrix.Multiply(ref rotationMatrix, ref mat, out mat);
			}
			
			public void invTrans(ref Matrix mat)
			{
				Matrix.Multiply(ref rotationMatrix, ref mat, out mat);
				Matrix.Multiply(ref offsetMatrix, ref mat, out mat);
			}
			
			public void updateMatrices()
			{
				Matrix.Translation(ref offset, out offsetMatrix);
				Matrix.RotationYawPitchRoll(rotation.Y, rotation.X, rotation.Z, out rotationMatrix);
				//Matrix.Multiply(ref offsetMatrix, ref rotationMatrix, out localTrans);
				
				Matrix.Invert(ref offsetMatrix, out offsetMatrixInv);
				Matrix.Invert(ref rotationMatrix, out rotationMatrixInv);
				//Matrix.Invert(ref localTrans, out localTransInv);
			}
		}
		
		public class Blend : ANamed
		{
			public OffRot or;
			
			public Segment parent;
			
			public int transIndex;
			
			public float prop;
			
			public Blend(string name, int tti, float propN, Segment parentN) : base(name)
			{
				transIndex = tti;
				prop = propN;
				parent = parentN;
			}
			
			public void update(ref Matrix trans, TransArr transArr)
			{
				or.updateMatrices();
				
				or.trans(ref trans);
				
				transArr.setValue(transIndex, ref trans);
				
				or.invTrans(ref trans);
			}
		}
		
		public class Segment : ANamed
		{
			public OffRot or;
			
			public Segment parent;
			public Model model;
			public List<Segment> segments;
			public List<Blend> blends;
			
			public BBox segBox;
			
			public int transIndex;
			
			public bool requiresUpdate;
			
			public Segment(string name) : base(name)
			{
				segments = new List<Segment>();
				blends = new List<Blend>();
			}
			
			public Segment(Segment gin, Segment parentN, Model modelN, List<Segment> allSegs) : this(gin.name)
			{
				requiresUpdate = true;
				
				foreach (Segment s in segments)
				{
					segments.Add(new Segment(s, this, modelN, allSegs));
				}
				parent = parentN;
				model = modelN;
				
				or = gin.or;
				segBox = gin.segBox;
				
				transIndex = gin.transIndex;
				
				allSegs[allSegs.IndexOf(gin)] = this;
				
				foreach (Blend b in gin.blends)
				{
					addBlend(b.name, b.transIndex, b.prop);
				}
			}
			
			public void addBlend(string name, int tti, float prop)
			{
				blends.Add(new Blend(name, tti, prop, this));
			}
			
			public void update(ref Matrix trans, TransArr transArr)
			{
				if (requiresUpdate)
				{
					foreach (Blend b in blends)
					{
						b.or.offset = or.offset * b.prop;
						b.or.rotation = or.rotation * b.prop;
						b.update(ref trans, transArr);
					}
					
					or.updateMatrices();
					
					or.trans(ref trans);
					
					transArr.setValue(transIndex, ref trans);
					
					foreach (Segment seg in segments)
					{
						seg.requiresUpdate = true;
						seg.update(ref trans, transArr);
					}
					
					requiresUpdate = false;
				}
				else
				{
					or.trans(ref trans);
					
					foreach (Segment seg in segments)
					{
						seg.update(ref trans, transArr);
					}
				}
				
				or.invTrans(ref trans);
			}
			
			public void createSegBox()
			{
				if (model.vertexType == VertexType.VertexPC)
				{
					createSegBoxVX_PC();
				}
				else if (model.vertexType == VertexType.VertexPCT)
				{
					createSegBoxVX_PCT();
				}
			}

			public void createSegBoxVX_PC()
			{
				segBox = new BBox();
				
				for (int i = model.numVertices - 1; i >= 0; i--)
				{
					if (model.verticesPC[i].tti == transIndex)
					{
						segBox.include(model.verticesPC[i].col3);
					}
				}
				
				segBox.fillVectors();
			}

			public void createSegBoxVX_PCT()
			{
				segBox = new BBox();
				
				for (int i = model.numVertices - 1; i >= 0; i--)
				{
					if (model.verticesPCT[i].tti == transIndex)
					{
						segBox.include(model.verticesPCT[i].col3);
					}
				}
				
				segBox.fillVectors();
			}
		}
		
		public class BBox
		{
			private readonly int[] bboxIndices = new int[] {
				0, 1, 2, // base
				0, 2, 3,

				4, 7, 6, // top
				4, 6, 5,

				0, 4, 5, // the others...
				0, 5, 1,

				1, 5, 6,
				1, 6, 2,

				2, 6, 7,
				2, 7, 3,

				3, 7, 4,
				3, 4, 0,
			};
			
			public bool empty;
			public float minX, minY, minZ;
			public float maxX, maxY, maxZ;
			public Vector3[] vecArr = new Vector3[8];
			
			public BBox()
			{
				empty = true;
			}
			
			BBox(Vector3 center, float xd, float yd, float zd)
			{
				minX = center.X - xd;
				minY = center.Y - yd;
				minZ = center.Z - zd;

				maxX = center.X + xd;
				maxY = center.Y + yd;
				maxZ = center.Z + zd;

				empty = false;
			}
			
			public bool inside(Vector3 point)
			{
				if (point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY && point.Z >= minZ && point.Z <= maxZ)
					return true;
				return false;
			}
			
			public bool overlap(BBox bbox)
			{
				if (bbox.minX < maxX && bbox.maxX > minX && bbox.minY < maxY && bbox.maxY > minY && bbox.minZ < maxZ && bbox.maxZ > minZ)
					return true;
				return false;
			}

			public bool dothSurviveClipTransformed(ref Matrix mat)
			{
				//return true;
				Vector4[] vecsA = new Vector4[8];

				// transform bbox
				transformVectors(ref mat, vecsA);

				float w;
				Vector4 curVec;

				// check everything
				for (int i = 0; i < 8; i++)
				{
					curVec = vecsA[i];

					w = curVec.W;
					curVec.X /= w;
					curVec.Y /= w;
					curVec.Z /= w;
					if (-1 <= curVec.X && curVec.X <= 1 && -1 <= curVec.Y && curVec.Y <= 1 && 0 <= curVec.Z && curVec.Z <= 1)
						return true;
				}

				// check everything
				for (int i = 0; i < 8; i++)
				{
					curVec = vecsA[i];

					w = curVec.W;
				}

				// try some other thing
				float aminX, aminY, aminZ, amaxX, amaxY, amaxZ;

				// get 2D box
				aminX = vecsA[0].X;
				amaxX = aminX;
				aminY = vecsA[0].Y;
				amaxY = aminY;
				aminZ = vecsA[0].Z;
				amaxZ = aminZ;
				for (int i = 1; i < 8; i++)
				{
					if (vecsA[i].X < aminX)
						aminX = vecsA[i].X;
					if (vecsA[i].X > amaxX)
						amaxX = vecsA[i].X;
					if (vecsA[i].Y < aminY)
						aminY = vecsA[i].Y;
					if (vecsA[i].Y > amaxY)
						amaxY = vecsA[i].Y;
					if (vecsA[i].Z < aminZ)
						aminZ = vecsA[i].Z;
					if (vecsA[i].Z > amaxZ)
						amaxZ = vecsA[i].Z;
				}

				if (-1 <= amaxX && aminX <= 1 && -1 <= amaxY && aminY <= 1 && 0 <= amaxZ && aminZ <= 1)
					return true;
				return false;
			}

			public bool overlapTransformed(BBox bbox, ref Matrix mat)
			{
				Vector4[] vecsA = new Vector4[8];

				// transform bbox
				transformVectors(ref mat, vecsA);

				float aminX, aminY, aminZ, amaxX, amaxY, amaxZ;

				// get 2D box
				aminX = vecsA[0].X;
				amaxX = aminX;
				aminY = vecsA[0].Y;
				amaxY = aminY;
				aminZ = vecsA[0].Z;
				amaxZ = aminZ;
				for (int i = 1; i < 8; i++)
				{
					if (vecsA[i].X < aminX)
						aminX = vecsA[i].X;
					if (vecsA[i].X > amaxX)
						amaxX = vecsA[i].X;
					if (vecsA[i].Y < aminY)
						aminY = vecsA[i].Y;
					if (vecsA[i].Y > amaxY)
						amaxY = vecsA[i].Y;
					if (vecsA[i].Z < aminZ)
						aminZ = vecsA[i].Z;
					if (vecsA[i].Z > amaxZ)
						amaxZ = vecsA[i].Z;
				}

				if (bbox.minX < amaxX && bbox.maxX > aminX && bbox.minY < amaxY && bbox.maxY > aminY && bbox.minZ < amaxZ && bbox.maxZ > aminZ)
					return true;
				return false;
			}

			// overlap in x.z plane - assumes projected result is a 2D bounding box, not for precision (no surprises there)
			public bool projectedBoundsOverlap(BBox bbox, ref Matrix mat)
			{
				Vector4[] vecsA = new Vector4[8];
				Vector4[] vecsB = new Vector4[8];

				// transform bboxes
				transformVectors(ref mat, vecsA);
				bbox.transformVectors(ref mat, vecsB);

				float aminX, aminZ, amaxX, amaxZ;
				float bminX, bminZ, bmaxX, bmaxZ;

				// get 2D boxes
				aminX = vecsA[0].X;
				amaxX = aminX;
				aminZ = vecsA[0].Z;
				amaxZ = aminZ;
				for (int i = 1; i < 8; i++)
				{
					if (vecsA[i].X < aminX)
						aminX = vecsA[i].X;
					if (vecsA[i].X > amaxX)
						amaxX = vecsA[i].X;
					if (vecsA[i].Z < aminZ)
						aminZ = vecsA[i].Z;
					if (vecsA[i].Z > amaxZ)
						amaxZ = vecsA[i].Z;
				}

				bminX = vecsB[0].X;
				bmaxX = bminX;
				bminZ = vecsB[0].Z;
				bmaxZ = bminZ;
				for (int i = 1; i < 8; i++)
				{
					if (vecsB[i].X < bminX)
						bminX = vecsB[i].X;
					if (vecsB[i].X > bmaxX)
						bmaxX = vecsB[i].X;
					if (vecsB[i].Z < bminZ)
						bminZ = vecsB[i].Z;
					if (vecsB[i].Z > bmaxZ)
						bmaxZ = vecsB[i].Z;
				}

				// see if boxes overlap
				if (bminX < amaxX && bmaxX > aminX && bminX < amaxX && bmaxX > aminX)
					return true;
				return false;
			}

			public bool collides(Ray ray)
			{
				float uRes, vRes, distRes; // not returned

				for (int i = 0; i < 36; i += 3)
				{
					if (ray.Intersects(ref vecArr[bboxIndices[i]], ref vecArr[bboxIndices[i + 1]], ref vecArr[bboxIndices[i + 2]]))
						return true;
				}

				return false;
			}
			
			public void transformVectors(ref Matrix mat, Vector4[] vecs)
			{
				Vector3.Transform(vecArr, ref mat, vecs);
				//for (int i = 0; i < 8; i++)
				//{
				//	D3DXVec3Transform(&(vecs[i]), &(vecArr[i]), mat);
				//}
			}

			public void fillVectors()
			{
				// boottom
				vecArr[0].X = minX;
				vecArr[0].Y = minY;
				vecArr[0].Z = minZ;

				vecArr[1].X = maxX;
				vecArr[1].Y = minY;
				vecArr[1].Z = minZ;
				
				vecArr[2].X = maxX;
				vecArr[2].Y = maxY;
				vecArr[2].Z = minZ;

				vecArr[3].X = minX;
				vecArr[3].Y = maxY;
				vecArr[3].Z = minZ;
				
				// top
				vecArr[4].X = minX;
				vecArr[4].Y = minY;
				vecArr[4].Z = maxZ;

				vecArr[5].X = maxX;
				vecArr[5].Y = minY;
				vecArr[5].Z = maxZ;
				
				vecArr[6].X = maxX;
				vecArr[6].Y = maxY;
				vecArr[6].Z = maxZ;

				vecArr[7].X = minX;
				vecArr[7].Y = maxY;
				vecArr[7].Z = maxZ;
			}

			public void include(BBox bbox, ref Matrix mat)
			{
				Vector4[] vecs = new Vector4[8];
				Vector4 curVec;
				
				if (bbox.empty)
				{
					return;
				}

				bbox.transformVectors(ref mat, vecs);

				for (int i = 0; i < 8; i++)
				{
					curVec = vecs[i];
					include(new Vector3(curVec.X, curVec.Y, curVec.Z));
				}
			}

			public void include(BBox bbox)
			{
				if (empty)
				{
					minX = bbox.minX;
					minY = bbox.minY;
					minZ = bbox.minZ;

					maxX = bbox.maxX;
					maxY = bbox.maxY;
					maxZ = bbox.maxZ;

					empty = false;
				}
				else
				{
					if (bbox.minX < minX)
						minX = bbox.minX;
					if (bbox.minY < minY)
						minY = bbox.minY;
					if (bbox.minZ < minZ)
						minZ = bbox.minZ;

					if (bbox.maxX > maxX)
						maxX = bbox.maxX;
					if (bbox.maxY > maxY)
						maxY = bbox.maxY;
					if (bbox.maxZ > maxZ)
						maxZ = bbox.maxZ;
				}
			}

			public void include(Vector3 vec)
			{
				if (empty)
				{
					minX = vec.X;
					minY = vec.Y;
					minZ = vec.Z;

					maxX = vec.X;
					maxY = vec.Y;
					maxZ = vec.Z;

					empty = false;
				}
				else
				{
					if (vec.X < minX)
						minX = vec.X;
					if (vec.Y < minY)
						minY = vec.Y;
					if (vec.Z < minZ)
						minZ = vec.Z;

					if (vec.X > maxX)
						maxX = vec.X;
					if (vec.Y > maxY)
						maxY = vec.Y;
					if (vec.Z > maxZ)
						maxZ = vec.Z;
				}
			}
		}
		
		public class TransArrBuffer
		{
			ConstBuffer<TransCData> transBuffer;
			
			public TransArrBuffer(Device device)
			{
				transBuffer = new ConstBuffer<TransCData>(device, TransCData.defaultSlot);
			}
			
			public unsafe void update(DeviceContext context)
			{
				transBuffer.update(context);
			}
			
			public void apply(DeviceContext context)
			{
				transBuffer.applyVStage(context);
			}
			
			public unsafe void setValues(int ttiOffset, TransArr transArr)
			{
				setLiteralValues(ttiOffset, transArr.getTransposedArr());
			}
			
			/// <summary>
			/// Does not perform transpose
			/// </summary>
			public unsafe void setLiteralValue(int tti, ref Matrix mat)
			{
				if (tti < 0 || tti >= TransCData.maxTransMats)
					throw new BloominEckException("You tryin' t' buffer overrun or summin'?");
				
				fixed (Matrix* matPtr = &transBuffer.data.mat0)
				{
					matPtr[tti] = mat;
				}
			}
			
			/// <summary>
			/// Does not perform transpose
			/// </summary>
			public unsafe void setLiteralValues(int ttiOffet, Matrix[] mats)
			{
				if (ttiOffet < 0 || ttiOffet + mats.Length > TransCData.maxTransMats)
					throw new BloominEckException("You tryin' t' buffer overrun or summin'?");
				
				fixed (Matrix* matPtr = &transBuffer.data.mat0)
				{
					for (int i = 0; i < mats.Length; i++)
					{
						matPtr[ttiOffet + i] = mats[i];
					}
				}
			}
		}
		
		// TODO: implement (and section.drawBatched)
		public class CompoundTransArrBuffer : TransArrBuffer
		{
			private int curOffset;
			public int count {get; private set;}
			
			public CompoundTransArrBuffer(Device device) : base(device)
			{
				curOffset = 0;
				count = 0;
			}
			
			public void append(TransArr transArr)
			{
				
			}
		}
		
		public class TransArr
		{
			private Matrix[] mats;
			private Matrix[] transposedMats;
			public int len {get; private set;}
			
			public Matrix[] getArr()
			{
				return mats;
			}
			
			public Matrix[] getClonedArr()
			{
				Matrix[] marr = new Matrix[len];
				for (int i = 0; i < len; i++)
				{
					marr[i] = mats[i];
				}
				return marr;
			}
			
			public Matrix[] getTransposedArr()
			{
				return transposedMats;
			}
			
			public Matrix[] getClonedTransposedArr()
			{
				Matrix[] marr = new Matrix[len];
				for (int i = 0; i < len; i++)
				{
					marr[i] = transposedMats[i];
				}
				return marr;
			}
			
			public void setValue(int tti, ref Matrix mat)
			{
				mats[tti] = mat;
				Matrix.Transpose(ref mat, out transposedMats[tti]);
			}
			
			public void getValue(int tti, out Matrix mat)
			{
				mat = mats[tti];
			}
			
			public Matrix getValue(int tti)
			{
				return mats[tti];
			}
			
			public void getTranposedValue(int tti, out Matrix mat)
			{
				mat = transposedMats[tti];
			}
			
			public Matrix getTranposedValue(int tti)
			{
				return transposedMats[tti];
			}
			
			public int getLen()
			{
				return len;
			}
			
			public void create(int lenN)
			{
				len = lenN;
				mats = new Matrix[len];
				transposedMats = new Matrix[len];
			}
		}
		
		public class SpriteArrBuffer
		{
			ConstBuffer<SpriteCData> spriteBuffer;
			
			public SpriteArrBuffer(Device device)
			{
				spriteBuffer = new ConstBuffer<SpriteCData>(device, SpriteCData.maxSprite4s);
			}
			
			public unsafe void setValues(int sdiOffset, SpriteArr spriteArr)
			{
				setLiteralValues(sdiOffset, spriteArr.getArr());
			}
			
			/// <summary>
			/// Does not perform transpose
			/// </summary>
			public unsafe void setLiteralValue(int sdi, ref Vector4 mat)
			{
				if (sdi < 0 || sdi > SpriteCData.maxSprite4s)
					throw new BloominEckException("You tryin' t' buffer overrun or summin'?");
				
				fixed (SpriteCData* scdPtr = &spriteBuffer.data)
				{
					Vector4* vecPtr = (Vector4*)scdPtr;
					vecPtr[sdi] = mat;
				}
			}
			
			/// <summary>
			/// Does not perform transpose
			/// </summary>
			public unsafe void setLiteralValues(int sdiOffet, Vector4[] vecs)
			{
				if (sdiOffet < 0 || sdiOffet + vecs.Length > SpriteCData.maxSprite4s)
					throw new BloominEckException("You tryin' t' buffer overrun or summin'?");
				
				fixed (SpriteCData* scdPtr = &spriteBuffer.data)
				{
					Vector4* vecPtr = (Vector4*)scdPtr;
					for (int i = 0; i < vecs.Length; i++)
					{
						vecPtr[sdiOffet + i] = vecs[i];
					}
				}
			}
		}
		
		public class CompoundSpriteArrBuffer : SpriteArrBuffer
		{
			private int curOffset;
			public int count {get; private set;}
			
			public CompoundSpriteArrBuffer(Device device) : base(device)
			{
				curOffset = 0;
				count = 0;
			}
			
			public void append(SpriteArr spriteArr)
			{
				// implement
			}
		}
		
		public class SpriteArr
		{
			private Vector4[] vecs;
			public int len {get; private set;}
			
			public Vector4[] getArr()
			{
				return vecs;
			}
			
			public Vector4[] getClonedArr()
			{
				Vector4[] varr = new Vector4[len];
				for (int i = 0; i < len; i++)
				{
					varr[i] = vecs[i];
				}
				return varr;
			}
			
			public void setValue(int sdi, ref Vector4 vec)
			{
				vecs[sdi] = vec;
			}
			
			public void getValue(int sdi, out Vector4 vec)
			{
				vec = vecs[sdi];
			}
			
			public Vector4 getValue(int sdi)
			{
				return vecs[sdi];
			}
			
			public int getLen()
			{
				return len;
			}
			
			public void create(int lenN)
			{
				len = lenN;
				vecs = new Vector4[len];
			}
		}
		
		// filled out by frame
		public class PreDrawData
		{
			public UN11 uneleven;
			
			
			public PreDrawData(UN11 unelevenN)
			{
				uneleven = unelevenN;
			}
		}
		
		// filled out by slide
		public class DrawData
		{
			public PreDrawData pddat;
			
			public SceneType sceneType;
			
			public TextureView targetTex;
			public RenderViewPair targetRenderViewPair;
			public TextureView sideTex;
			public RenderViewPair sideRenderViewPair;
			
			public ConstBuffer<EyeCData> eyeBuffer;
			public ConstBuffer<TargetCData> targetBuffer;
			public ConstBuffer<LightMapCData> lightMapBuffer;
			
			public AppliableViewport vp;
			public Matrix viewProjVP; // mostly for good measure
			
			public Overness overness;
			
			public LightList lights;
			
			public DrawData(PreDrawData pddatN, SceneType sceneTypeN)
			{
				pddat = pddatN;
				
				sceneType = sceneTypeN;
			}
		}
		
		public abstract class GeometryDrawData
		{
			public abstract void drawGeometry(DeviceContext context, DrawData ddat);
		}
		
		public class GeometryDrawDataList : List<GeometryDrawData>
		{
		}
		
		//
		// test geometry
		//
		public class CubeDrawData : GeometryDrawData
		{
			public Cube cube;
			
			public CubeDrawData(Cube cubeN)
			{
				cube = cubeN;
			}
			
			public override void drawGeometry(DeviceContext context, UN11.DrawData ddat)
			{
				cube.draw(context, this, ddat);
			}
		}
		
		public class Cube : ANamed
		{
			Buffer vbuff;
			
			public Cube(Device device) : base("TestCube")
			{
				vbuff = Buffer.Create(device, BindFlags.VertexBuffer, new UN11.VertexPC[]
				                      {
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)), // Front
				                      	new UN11.VertexPC(new Vector4(-1.0f,  1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
				                      	
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)), // BACK
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,  1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, -1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
				                      	
				                      	new UN11.VertexPC(new Vector4(-1.0f, 1.0f, -1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)), // Top
				                      	new UN11.VertexPC(new Vector4(-1.0f, 1.0f,  1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, 1.0f,  1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f, 1.0f, -1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, 1.0f,  1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, 1.0f, -1.0f,  1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
				                      	
				                      	new UN11.VertexPC(new Vector4(-1.0f,-1.0f, -1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)), // Bottom
				                      	new UN11.VertexPC(new Vector4( 1.0f,-1.0f,  1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,-1.0f,  1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,-1.0f, -1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,-1.0f, -1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,-1.0f,  1.0f,  1.0f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f)),
				                      	
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)), // Left
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f,  1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,  1.0f,  1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,  1.0f,  1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4(-1.0f,  1.0f, -1.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f, 1.0f)),
				                      	
				                      	new UN11.VertexPC(new Vector4( 1.0f, -1.0f, -1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)), // Right
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, -1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f, -1.0f, -1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f, -1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)),
				                      	new UN11.VertexPC(new Vector4( 1.0f,  1.0f,  1.0f, 1.0f), new Vector4(0.0f, 1.0f, 1.0f, 1.0f)),
				                      });
			}
			
			public void draw(DeviceContext context, CubeDrawData cddat, DrawData ddat)
			{
				ddat.pddat.uneleven.techniques.Get("dull2").passes[0].apply(context);
				context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
				context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vbuff, UN11.VertexPC.size, 0));
				ddat.targetRenderViewPair.apply(context, false, false);
				ddat.eyeBuffer.applyVStage(context);
				ddat.eyeBuffer.update(context);
				ddat.pddat.uneleven.blendStates.none.apply(context);
				ddat.pddat.uneleven.depthStencilStates.zReadWrite.apply(context);
				ddat.pddat.uneleven.rasterizerStates.ccFrontcull.apply(context);
				
				context.Draw(36, 0);
			}
		}
		// end test geometry
		
		public class Model : ANamed
		{
			public Buffer vbuff;
			public VertexBufferBinding vbuffBinding;
			public Buffer ibuff;
			public VertexType vertexType;
			public int stride;
			public int numVertices;
			public int numIndices;
			
			public TransArr transArr;
			public TransArrBuffer transArrBuffer;
			
			public List<Segment> segments;
			public List<Segment> allSegs;
			public List<Section> sections;
			
			public int highTti;
			public int batchCopies;
			public VertexPC[] verticesPC;
			public VertexPCT[] verticesPCT;
			public short[] indices;
			
			public BBox modelBox;
			public bool noCull;
			
			public Model(string name) : base(name)
			{
				segments = new List<Segment>();
				allSegs = new List<Segment>();
				sections = new List<Section>();
			}
			
			public Model(Model gin, Device device, DeviceContext context, bool createOwnSections) : this(gin.name)
			{
				foreach (Segment s in gin.allSegs)
				{
					allSegs.Add(s);
				}
				foreach (Segment s in gin.segments)
				{
					segments.Add(new Segment(s, s.parent, this, allSegs));
				}
				foreach (Section s in gin.sections)
				{
					if (createOwnSections)
						sections.Add(new Section(device, s));
					else
						sections.Add(s);
				}
				
				batchCopies = gin.batchCopies;
				vbuff = gin.vbuff;
				vbuffBinding = gin.vbuffBinding;
				ibuff = gin.ibuff;
				verticesPC = gin.verticesPC;
				verticesPCT = gin.verticesPCT;
				indices = gin.indices;
				stride = gin.stride;
				vertexType = gin.vertexType;
				numVertices = gin.numVertices;
				numIndices = gin.numIndices;
				noCull = gin.noCull;
				highTti = gin.highTti;
				
				transArr = new TransArr();
				transArrBuffer = new TransArrBuffer(device);
				transArr.create(highTti + 1);
				createSegmentBoxes();
			}
			
			public Section getSec(string name)
			{
				foreach (Section s in sections)
				{
					if (s.name == name)
						return s;
				}
				
				return null;
			}
			
			public Segment getSeg(string name)
			{
				foreach (Segment s in allSegs)
				{
					if (s.name == name)
						return s;
				}
				
				return null;
			}
			
			public int getSegTti(string name)
			{
				foreach (Segment s in allSegs)
				{
					if (s.name == name)
						return s.transIndex;
				}
				
				return -1;
			}
			
			public void createSegmentBoxes()
			{
				foreach (Segment s in allSegs)
				{
					s.createSegBox();
				}
			}
			
			public void update(ref Matrix trans, bool forceUpdate = false)
			{
				// update sections
				foreach (Section sec in sections)
				{
					sec.update();
				}
				
				// build transarr / update segments
				foreach (Segment seg in segments)
				{
					if (forceUpdate)
						seg.requiresUpdate = true;
					seg.update(ref trans, transArr);
				}
				
				transArr.setValue(0, ref trans);
				
				transArrBuffer.setValues(0, transArr);
				
				// sort out bounding boxes
				modelBox = new BBox();
				
				foreach (Segment seg in allSegs)
				{
					Matrix segTransMat = transArr.getValue(seg.transIndex);
					modelBox.include(seg.segBox, ref segTransMat);
				}
				
				modelBox.fillVectors();
			}
			
			public void draw(DeviceContext context, DrawData ddat)
			{
				if (noCull || modelBox.dothSurviveClipTransformed(ref ddat.viewProjVP))
					goto notOcced;
				return;
				
			notOcced:
				
				context.InputAssembler.SetIndexBuffer(ibuff, Format.R16_UInt, 0);
				context.InputAssembler.SetVertexBuffers(0, vbuffBinding);
				
				transArrBuffer.update(context);
				transArrBuffer.apply(context);
				
				foreach (Section sec in sections)
				{
					sec.draw(context, ddat);
				}
			}
			
			public void drawMany(DeviceContext context, ManyModelDrawData mmddat, DrawData ddat)
			{
				bool cullall = true;
				
				foreach (Model m in mmddat.models)
				{
					bool cullm = true; // true means it will be culled
					
					if (noCull || m.modelBox.dothSurviveClipTransformed(ref ddat.viewProjVP))
					{
						cullm = false;
						cullall = false;
					}
					
					foreach (Section sec in m.sections)
						sec.curDrawCull = cullm; // TODO: move this into model?
				}
				
				if (cullall)
					return;
				
				context.InputAssembler.SetIndexBuffer(ibuff, Format.R16_UInt, 0);
				context.InputAssembler.SetVertexBuffers(0, vbuffBinding);
				
				// set transArr in Section.drawMany
				for (int i = 0; i < sections.Count; i++)
				{
					sections[i].drawMany(context, ddat, mmddat, i);
				}
			}
			
			public unsafe void fillVBuff(DeviceContext context)
			{
				if (vertexType == VertexType.VertexPC)
					fillVBuffPC(context);
				if (vertexType == VertexType.VertexPCT)
					fillVBuffPCT(context);
			}
			
			private unsafe void fillVBuffPC(DeviceContext context)
			{
				DataStream dstream;
				context.MapSubresource(vbuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
				
				byte* buffPtr = (byte*)dstream.DataPointer;
				fixed (VertexPC* vertexPtrVPC = verticesPC)
				{
					byte* verticesPtr = (byte*)vertexPtrVPC;
					
					if (batchCopies == 1)
					{
						Utils.copy(verticesPtr, buffPtr, numVertices * stride);
					}
					else
					{
						int ttiOffset = 0;

						// madness ensues
						for (int i = 0; i < batchCopies; i++)
						{
							byte* copyPtr = (byte*)buffPtr + i * numVertices * stride;
							Utils.copy(copyPtr, buffPtr, numVertices * stride);
							
							// sort out ttiOffset for batch copies
							if (i > 0)
							{
								ttiOffset += highTti + 1; // 1 makes it the count

								VertexPC* vPCs = (VertexPC*)copyPtr;
								for (int j = 0; j < numVertices; j++)
								{
									vPCs[j].tti += ttiOffset;
								}
							}
						}
					}
				}
				
				dstream.Dispose();
				context.UnmapSubresource(vbuff, 0);
			}
			
			private unsafe void fillVBuffPCT(DeviceContext context)
			{
				DataStream dstream;
				context.MapSubresource(vbuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
				
				byte* buffPtr = (byte*)dstream.DataPointer;
				fixed (VertexPCT* vertexPtrVPCT = verticesPCT)
				{
					byte* verticesPtr = (byte*)vertexPtrVPCT;
					
					if (true || batchCopies == 1)
					{
						Utils.copy(verticesPtr, buffPtr, numVertices * stride);
					}
					else
					{
						int ttiOffset = 0;

						// madness ensues
						for (int i = 0; i < batchCopies; i++)
						{
							byte* copyPtr = (byte*)buffPtr + i * numVertices * stride;
							Utils.copy(copyPtr, buffPtr, numVertices * stride);
							
							// sort out ttiOffset for batch copies
							if (i > 0)
							{
								ttiOffset += highTti + 1; // 1 makes it the count

								VertexPCT* vPCTs = (VertexPCT*)copyPtr;
								for (int j = 0; j < numVertices; j++)
								{
									vPCTs[j].tti += ttiOffset;
								}
							}
						}
					}
				}
				
				dstream.Dispose();
				context.UnmapSubresource(vbuff, 0);
			}
			
			public void createVBuff(Device device, DeviceContext context, VertexPC[] vPCs, VertexPCT[] vPCTs /*add formats here as appropriate, hope there arn't too many*/)
			{
				if (vertexType == VertexType.VertexPC)
				{
					vbuff = new Buffer(device, new BufferDescription(numVertices * VertexPC.size * batchCopies, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride));
					
//					vbuff = Buffer.Create(device, BindFlags.VertexBuffer, vPCs);
					
					vbuffBinding = new VertexBufferBinding(vbuff, UN11.VertexPC.size, 0);
					
					verticesPC = new VertexPC[vPCs.Length];
					Utils.copy<VertexPC>(0, vPCs, 0, verticesPC, vPCs.Length);
				}
				else if (vertexType == VertexType.VertexPCT)
				{
					vbuff = new Buffer(device, new BufferDescription(numVertices * VertexPCT.size * batchCopies, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride));
					
//					vbuff = Buffer.Create(device, BindFlags.VertexBuffer, vPCTs);
					
					vbuffBinding = new VertexBufferBinding(vbuff, UN11.VertexPCT.size, 0);
					
					verticesPCT = new VertexPCT[vPCTs.Length];
					Utils.copy<VertexPCT>(0, vPCTs, 0, verticesPCT, vPCTs.Length);
				}
				
				fillVBuff(context);
			}
			
			public unsafe void fillIBuff(DeviceContext context)
			{
				
				
				DataStream dstream;
				context.MapSubresource(ibuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
				
				byte* buffPtr = (byte*)dstream.DataPointer;
				fixed (short* indicesPtrShort = indices)
				{
					byte* indicesPtr = (byte*)indicesPtrShort;
					
					if (batchCopies == 1)
					{
						Utils.copy(indicesPtr, buffPtr, numIndices * sizeof(short));
						//Utils.copy(buffPtr, indicesPtr, numIndices * sizeof(short)); // this is the wrong way round!
					}
					else
					{
						// madness ensues
						foreach (Section sec in sections)
						{
							byte* secPtr = (byte*)buffPtr + sec.indexOffset * sizeof (short) * batchCopies;

							int idxOffset = 0;
							for (int i = 0; i < batchCopies; i++)
							{
								byte* copyPtr = secPtr + i * sec.triCount * 3 * sizeof (short);
								Utils.copy(indicesPtr + sec.indexOffset, copyPtr, sec.triCount * 3 * sizeof (short));
								//Utils.copy(copyPtr, indicesPtr + sec.indexOffset, sec.triCount * 3 * sizeof (short)); // is this the wrong way round??
								
								if (i > 0)
								{
									idxOffset += numVertices;
									
									short* idxs = (short*)copyPtr;
									for (int j = 0; j < sec.triCount * 3; j++)
									{
										idxs[j] += (short)idxOffset;
									}
								}
							}
						}
					}
				}
				
				dstream.Dispose();
				context.UnmapSubresource(ibuff, 0);
			}
			
			public void createIBuff(Device device, DeviceContext context, short[] ids)
			{
				ibuff = new Buffer(device, new BufferDescription(numIndices * sizeof (short) * batchCopies, ResourceUsage.Dynamic, BindFlags.IndexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, sizeof(short)));
				
//				ibuff = Buffer.Create(device, BindFlags.IndexBuffer, ids);
				
				indices = new short[ids.Length];
				Utils.copy<short>(0, ids, 0, indices, ids.Length);
				
				fillIBuff(context);
			}
			
//			public unsafe void fillVBuff(DeviceContext context)
//			{
//				if (vertexType == VertexType.VertexPC)
//					fillVBuffPC(context);
//				if (vertexType == VertexType.VertexPCT)
//					fillVBuffPCT(context);
//			}
//
//			private unsafe void fillVBuffPC(DeviceContext context)
//			{
//				DataStream dstream;
//				context.MapSubresource(vbuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
//
//				byte* buffPtr = (byte*)dstream.DataPointer;
//				fixed (VertexPC* vertexPtrVPC = verticesPC)
//				{
//					byte* verticesPtr = (byte*)vertexPtrVPC;
//
//					if (batchCopies == 1)
//					{
//						Utils.copy(verticesPtr, buffPtr, numVertices * stride);
//					}
//					else
//					{
//						int ttiOffset = 0;
//
//						// madness ensues
//						for (int i = 0; i < batchCopies; i++)
//						{
//							byte* copyPtr = (byte*)buffPtr + i * numVertices * stride;
//							Utils.copy(copyPtr, buffPtr, numVertices * stride);
//
//							// sort out ttiOffset for batch copies
//							if (i > 0)
//							{
//								ttiOffset += highTti + 1; // 1 makes it the count
//
//								VertexPC* vPCs = (VertexPC*)copyPtr;
//								for (int j = 0; j < numVertices; j++)
//								{
//									vPCs[j].tti += ttiOffset;
//								}
//							}
//						}
//					}
//				}
//
//				context.UnmapSubresource(vbuff, 0);
//			}
//
//			private unsafe void fillVBuffPCT(DeviceContext context)
//			{
//				DataStream dstream;
//				context.MapSubresource(vbuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
//
//				byte* buffPtr = (byte*)dstream.DataPointer;
//				fixed (VertexPCT* vertexPtrVPCT = verticesPCT)
//				{
//					byte* verticesPtr = (byte*)vertexPtrVPCT;
//
//					if (true || batchCopies == 1)
//					{
//						Utils.copy(verticesPtr, buffPtr, numVertices * stride);
//					}
//					else
//					{
//						int ttiOffset = 0;
//
//						// madness ensues
//						for (int i = 0; i < batchCopies; i++)
//						{
//							byte* copyPtr = (byte*)buffPtr + i * numVertices * stride;
//							Utils.copy(copyPtr, buffPtr, numVertices * stride);
//
//							// sort out ttiOffset for batch copies
//							if (i > 0)
//							{
//								ttiOffset += highTti + 1; // 1 makes it the count
//
//								VertexPCT* vPCTs = (VertexPCT*)copyPtr;
//								for (int j = 0; j < numVertices; j++)
//								{
//									vPCTs[j].tti += ttiOffset;
//								}
//							}
//						}
//					}
//				}
//
//				context.UnmapSubresource(vbuff, 0);
//			}
//
//			public void createVBuff(Device device, DeviceContext context, VertexPC[] vPCs, VertexPCT[] vPCTs /*add formats here as appropriate, hope there arn't too many*/)
//			{
//				if (vertexType == VertexType.VertexPC)
//				{
//					vbuff = new Buffer(device, new BufferDescription(numVertices * VertexPC.size * batchCopies, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride));
//
//					vbuffBinding = new VertexBufferBinding(vbuff, UN11.VertexPC.size, 0);
//
//					verticesPC = new VertexPC[vPCs.Length];
//					Utils.copy<VertexPC>(0, vPCs, 0, verticesPC, vPCs.Length);
//				}
//				else if (vertexType == VertexType.VertexPCT)
//				{
//					vbuff = new Buffer(device, new BufferDescription(numVertices * VertexPCT.size * batchCopies, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride));
//
//					vbuffBinding = new VertexBufferBinding(vbuff, UN11.VertexPCT.size, 0);
//
//					verticesPCT = new VertexPCT[vPCTs.Length];
//					Utils.copy<VertexPCT>(0, vPCTs, 0, verticesPCT, vPCTs.Length);
//				}
//
//				fillVBuff(context);
//			}
//
//			public unsafe void fillIBuff(DeviceContext context)
//			{
//				DataStream dstream;
//				context.MapSubresource(ibuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
//
//				byte* buffPtr = (byte*)dstream.DataPointer;
//				fixed (short* indicesPtrShort = indices)
//				{
//					byte* indicesPtr = (byte*)indicesPtrShort;
//
//					if (true || batchCopies == 1)
//					{
//						Utils.copy(indicesPtr, buffPtr, numIndices * sizeof(short));
//						//Utils.copy(buffPtr, indicesPtr, numIndices * sizeof(short)); // this is the wrong way round!
//					}
//					else
//					{
//						// madness ensues
//						foreach (Section sec in sections)
//						{
//							byte* secPtr = (byte*)buffPtr + sec.indexOffset * sizeof (short) * batchCopies;
//
//							int idxOffset = 0;
//							for (int i = 0; i < batchCopies; i++)
//							{
//								byte* copyPtr = secPtr + i * sec.triCount * 3 * sizeof (short);
//								Utils.copy(indicesPtr + sec.indexOffset, copyPtr, sec.triCount * 3 * sizeof (short));
//								//Utils.copy(copyPtr, indicesPtr + sec.indexOffset, sec.triCount * 3 * sizeof (short)); // is this the wrong way round??
//
//								if (i > 0)
//								{
//									idxOffset += numVertices;
//
//									short* idxs = (short*)copyPtr;
//									for (int j = 0; j < sec.triCount * 3; j++)
//									{
//										idxs[j] += (short)idxOffset;
//									}
//								}
//							}
//						}
//					}
//				}
//
//				context.UnmapSubresource(ibuff, 0);
//			}
//
//			public void createIBuff(Device device, DeviceContext context, short[] ids)
//			{
//				ibuff = new Buffer(device, new BufferDescription(numIndices * sizeof (short) * batchCopies, ResourceUsage.Dynamic, BindFlags.IndexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride));
//
//				indices = new short[ids.Length];
//				Utils.copy<short>(0, ids, 0, indices, ids.Length);
//
//				fillIBuff(context);
//			}
		}
		
		public class ModelCollection : NamedCollection<Model>
		{
		}
		
		public class ModelList : List<Model>
		{
		}
		
		public class EntityCollection : NamedCollection<Entity>
		{
		}
		
		public abstract class Entity : ANamed
		{
			public Entity(string name) : base(name)
			{
				// joy
			}
			
			public abstract void update(bool forceUpdate);
		}
		
		public class ModelEntityDrawData : GeometryDrawData
		{
			public ModelEntity mEnt;
			
			public ModelEntityDrawData(ModelEntity mEntN)
			{
				mEnt = mEntN;
			}
			
			public override void drawGeometry(DeviceContext context, UN11.DrawData ddat)
			{
				mEnt.draw(context, this, ddat);
			}
		}
		
		public class ModelEntity : Entity
		{
			public Model mdl;
			public OffRot or;
			
			public ModelEntity(Model mdlN, string name) : base(name)
			{
				mdl = mdlN;
			}
			
			public void draw(DeviceContext context, ModelEntityDrawData meddat, DrawData ddat)
			{
				mdl.draw(context, ddat);
			}
			
			public override void update(bool forceUpdate = false)
			{
				or.updateMatrices();
				
				Matrix trans = or.transMat;
				
				mdl.update(ref trans, forceUpdate);
			}
		}
		
		public class ManyModelDrawData : GeometryDrawData
		{
			public Model mdl;
			public ModelList models = new ModelList();
			public bool useOwnSections = true; // if you set this to false, you probably want to be batching
			
			public ManyModelDrawData(Model mdlN)
			{
				mdl = mdlN;
			}
			
			public override void drawGeometry(DeviceContext context, UN11.DrawData ddat)
			{
				mdl.drawMany(context, this, ddat);
			}
		}
		
		public class MatrixCollection : NamedCollection<NamedMatrix>
		{
		}
		
		public class NamedMatrix : ANamed
		{
			public Matrix mat;
			
			public NamedMatrix(string name) : base(name)
			{
				// joy
			}
			
			public NamedMatrix(string name, Matrix matN) : base(name)
			{
				mat = matN;
			}
		}
		
		// events
		public enum EventType : int
		{
			tick = 0,
			mouse = 1,
			key = 2,
			cmd = 3,
			focus = 4
		}
		
		public class EventState
		{
			private bool[] keyStates = new bool[255];
			public EventHandler<FocusEvent> focus {get; private set;}
			
			public EventState()
			{
				focus = null;
			}
			
			public bool keyDown(Keys k)
			{
				return keyStates[(int)k];
			}
		}
		
		public class EventQueue : Queue<Event>
		{
		}
		
		public interface Event
		{
			EventType type {get;}
			
			Event deChild();
			void enChild(Event e);
		}
		
		public abstract class AEvent : Event
		{
			public EventType type {get; private set;}
			
			private EventQueue children = null;
			
			public Event deChild()
			{
				if (children == null || children.Count == 0)
					return null;
				
				return children.Dequeue();
			}
			
			public void enChild(Event e)
			{
				if (children == null)
					children = new EventQueue();
				
				children.Enqueue(e);
			}
			
			public AEvent(EventType typeN)
			{
				type = typeN;
			}
		}
		
		public class TickEvent : AEvent
		{
			public long time {get; private set;}
			
			public TickEvent(long timeN) : base(EventType.tick)
			{
				time = timeN;
			}
		}
		
		public class MouseEvent : AEvent
		{
			public MouseEvent() : base(EventType.mouse)
			{
				
			}
		}
		
		public class KeyEvent : AEvent
		{
			public KeyEvent() : base(EventType.key)
			{
				
			}
		}
		
		public class CmdEvent : AEvent
		{
			public CmdEvent() : base(EventType.cmd)
			{
				
			}
		}
		
		public class FocusEvent : AEvent
		{
			public Event underlyingEvent {get; private set;} //?
			
			public FocusEvent() : base(EventType.focus)
			{
				
			}
		}
		
		// this is pretty ugly
		public class EventTypedHandlerCollection : Dictionary<EventType, EventHandler>
		{
			public EventTypedHandlerCollection()
			{
				Add<TickEvent>(new EventHandlerList<TickEvent>(EventType.tick));
				Add<MouseEvent>(new EventHandlerList<MouseEvent>(EventType.mouse));
				Add<KeyEvent>(new EventHandlerList<KeyEvent>(EventType.key));
				Add<CmdEvent>(new EventHandlerList<CmdEvent>(EventType.cmd));
			}
			
			public void handle(Event e)
			{
				this[e.type].handle(e);
			}
			
			public void register<ET>(EventType type, EventHandler<ET> eh) where ET : Event
			{
				((EventHandlerList<ET>)this[type]).register(eh);
			}
			
			private void Add<ET>(EventHandlerList<ET> ehl) where ET : Event
			{
				Add(ehl.type, ehl);
			}
		}
		
		public class EventHandlerList<ET> : List<EventHandler<ET>>, EventHandler where ET : Event
		{
			public EventType type {get; private set;}
			
			public EventHandlerList(EventType typeN)
			{
				type = typeN;
			}
			
			public void register(EventHandler<ET> eh)
			{
				this.Add(eh);
			}
			
			public void handle(Event e)
			{
				foreach (EventHandler<ET> eh in this)
					eh.handle((ET)e);
			}
		}
		
		public interface EventHandler
		{
			void handle(Event e);
		}
		
		public interface EventHandler<ET> where ET : Event
		{
			void handle(ET e);
		}
		
		public class EventManager
		{
			private EventQueue due = new EventQueue();
			private EventTypedHandlerCollection handlers = new EventTypedHandlerCollection();
			
			public void queueEvent(Event e)
			{
				due.Enqueue(e);
			}
			
			public void register<ET>(EventHandler<ET> eh, EventType type) where ET : Event
			{
				handlers.register<ET>(type, eh);
			}
			
			public void handleAllEvents()
			{
				while (due.Count > 0)
					handleEvent(due.Dequeue());
			}
			
			private void handleEvent(Event e)
			{
				handlers.handle(e);
				
				Event ce;
				while ((ce = e.deChild()) != null)
				{
					handleEvent(ce);
				}
			}
		}
		
		// slides
		public class SlideDrawDataList : List<SlideDrawData>
		{
		}
		
		public abstract class SlideDrawData
		{
			public abstract void drawSlide(DeviceContext context, PreDrawData pddat);
		}
		
		public class SlideList : NamedList<Slide>
		{
		}
		
		public interface Slide : Named
		{
		}
		
		public class ViewTrans
		{
			public float bbuffWidth, bbuffHeight;
			public float textScaleX, textScaleY;
			public float invTextScaleX, invTextScaleY;
			public float winWidth, winHeight;
			public float centreX, centreY;
			public float scaleX, scaleY;
			public float invScaleX, invScaleY;
			
			public ViewTrans(float bbuffSizeX, float bbuffSizeY, float winSizeX, float winSizeY)
			{
				bbuffWidth = bbuffSizeX;
				bbuffHeight = bbuffSizeY;
		
				winWidth = winSizeX;
				winHeight = winSizeY;
		
				textScaleX = bbuffWidth / winWidth;
				textScaleY = bbuffHeight / winHeight;
				invTextScaleX = 1.0f / textScaleX;
				invTextScaleY = 1.0f / textScaleY;
				
				centreX = winSizeX / 2.0f;
				centreY = winSizeY / 2.0f;
				invScaleX = centreX;
				invScaleY = -centreY;
				scaleX = 1.0f / invScaleX;
				scaleY = 1.0f / invScaleY;
			}
		
			public float xToTextX(float x)
			{
				return x * textScaleX;
			}
		
			public float yToTextY(float y)
			{
				return y * textScaleY;
			}
		
			public float xToScreen(float x)
			{
				return (x - centreX) * scaleX;
			}
		
			public float xToWindow(float x)
			{
				return x * invScaleX + centreX;
			}
		
			public float yToScreen(float y)
			{
				return (y - centreY) * scaleY;
			}
		
			public float yToWindow(float y)
			{
				return y * invScaleY + centreY;
			}
		
			public float wToScreen(float w)
			{
				return w * scaleX;
			}
		
			public float hToScreen(float h)
			{
				return h * -scaleY;
			}
		}
		
		public abstract class ASlide : Slide
		{
			public string name {get; private set;}
			
			public ASlide(string nameN)
			{
				name = nameN;
			}
		}
		
		public class FaceDrawData : SlideDrawData
		{
			public Face face;
			public ViewTrans viewTrans;
			
			public ElemList elems = new ElemList();
			
			public FaceDrawData(Face faceN, ViewTrans viewTransN)
			{
				face = faceN;
				viewTrans = viewTransN;
			}
			
			public override void drawSlide(DeviceContext context, PreDrawData pddat)
			{
				face.draw(context, this, pddat);
			}
		}
		
		public class ElemDrawData
		{
			public ConstBuffer<SectionCData> sectionBuffer;
			
			public Buffer vbuff;
			public VertexBufferBinding vbuffBinding;
			
			public ElemDrawData(Device device)
			{
				sectionBuffer = new ConstBuffer<SectionCData>(device, SectionCData.defaultSlot);
				
				vbuff = new Buffer(device, new BufferDescription(4 * VertexPCT.size, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, VertexPCT.size));
				
				vbuffBinding = new VertexBufferBinding(vbuff, UN11.VertexPCT.size, 0);
			}
		}
		
		public class ElemCollection : NamedCollection<IElem>
		{
		}
		
		public class ElemList : NamedList<IElem>
		{
		}
		
		// TODO: make this better (someone wrote from Barembs)
		public interface IElem : Named
		{
			bool enabled {get;}
			bool visible {get;}
			bool clickable {get;}
			bool tapChildren {get;}
			
			IElem parent {get;}
			
			void update(float offsetX, float offsetY, ViewTrans vt, bool force);
			void draw(DeviceContext context, ElemDrawData Eddat, PreDrawData pddat);
			bool getTaped(float x, float y, out IElem taped, out float xOut, out float yOut);
		}
		
		public abstract class AElem : IElem
		{
			public string name {get; private set;}
			public IElem parent {get; private set;} // null is acceptable
			public bool needsUpdate;
			
			public bool enabled {get; set;}
			public bool visible {get; set;}
			public bool clickable {get; set;}
			
			public bool tapChildren {get; set;}
			public bool updateChildren {get; set;}
			public bool drawChildren {get; set;}
			
			public Rectangle rect;
			
			// clc
			public RectangleF clcRect;
			// end clc
			
			private ElemList elems = new ElemList();
			
			public AElem(string nameN, IElem parentN, Rectangle rectN)
			{
				name = nameN;
				parent = parentN;
				
				rect = rectN;
				
				enabled = true;
				visible = true;
				clickable = true;
				
				tapChildren = true;
				updateChildren = true;
				drawChildren = true;
				
				
				needsUpdate = true;
			}
			
			public void update(ViewTrans vt, bool force)
			{
				update(0f, 0f, vt, force);
			}
			
			public void update(float offsetX, float offsetY, ViewTrans vt, bool force)
			{
				if (!visible)
					return;
				
				// force is passed down to children
				force = force || needsUpdate;
				
				if (force)
				{
					clcRect.Left = rect.Left + offsetX;
					clcRect.Right = rect.Right + offsetX;
					clcRect.Top = rect.Top + offsetY;
					clcRect.Bottom = rect.Bottom + offsetY;
					
					updateMe(vt);
				}
				
				if (updateChildren)
				{
					foreach (IElem ce in elems)
					{
						generateChildOffset(ref offsetX, ref offsetY);
						ce.update(offsetX, offsetY, vt, force);
					}
				}
			}
			
			protected virtual void generateChildOffset(ref float offsetX, ref float offsetY)
			{
				offsetX = rect.Left;
				offsetY = rect.Top;
			}
			
			public void draw(DeviceContext context, ElemDrawData eddat, PreDrawData pddat)
			{
				if (!visible)
					return;
				
				drawMe(context, eddat, pddat);
				
				if (drawChildren)
				{
					foreach (IElem ce in elems)
					{
						ce.draw(context, eddat, pddat);
					}
				}
			}
			
			protected abstract void updateMe(ViewTrans vt);
			protected abstract void drawMe(DeviceContext context, ElemDrawData eddat, PreDrawData pddat);

			public bool getTaped(float x, float y, out IElem taped, out float xOut, out float yOut)
			{
				if (!enabled || !visible) // can't click disabled / invisble stuff
					goto notap;
		
				if (x >= clcRect.Left && x <= clcRect.Right && y >= clcRect.Top && y <= clcRect.Bottom)
				{
					if (tapChildren)
					{
						foreach (IElem ce in elems.EnumerateBackwards())
						{
							if (ce.enabled && ce.getTaped(x, y, out taped, out xOut, out yOut))
							{
								return true; // stop on the first child to be taped
							}
						}
					}
		
					if (clickable)
					{
						// no children taped, return me
						xOut = x - clcRect.Left;
						yOut = y - clcRect.Top;
						taped = this;
						return true;
					}
				}
		
			notap:
				
				xOut = 0;
				yOut = 0;
				taped = null;
				return false;
			}
		}
		
		public enum TexAlign : uint
		{
			// tex alignment and modes (2 bits of horizontal, 2 bits for verticle, 2 bits for other stuff)
			Horizontal = 3,
			Fillh = 0,
			Left = 1,
			Right = 2,
			Center = 3,
			
			Verticle = 12,
			Fillv = 0,
			Top = 4,
			Bottom = 8,
			Middle = 12,
			
			OffsetInset = 16,
			
			PixelOffset = 32,
		}
		
		public enum TexMode : byte
		{
			Fit = 1, // rejects alignment, and everything else really, SIMPLE and CHEAP
			Zoom = 2, // requires image dimensions, fits inside box
			Flat = 3, // requires image dimensions, no scaling
		}
		
		public class TexElem : AElem
		{
			public Texness texness;
			public Technique tech;
			
			public Vector4 colmod;
			
			public float texW;
			public float texH;
			public float texScaleX;
			public float texScaleY;
			public TexAlign texAlign;
			public TexMode texMode;
			public float texHAlignOffset; // flex only
			public float texVAlignOffset;
			
			// clc - this is stuff that is calculated in update()
			public VertexPCT[] clcTexVerts;
			public Vector4 clcTexData;
			public bool clcUseTexData;
			// end clc
			
			public TexElem(string nameN, IElem parentN, Rectangle rectN) : base(nameN, parentN, rectN)
			{
				texW = -1; // means to assume we don't have this data (may be ignored)
				texH = -1;
				
				texMode = TexMode.Fit; // cheapest, doesn't need to know image dimensions, probably what everyone wants
				texAlign = TexAlign.Fillh | TexAlign.Fillv; // (0)
				texScaleX = 1.0f;
				texScaleY = 1.0f;
				texHAlignOffset = 0.0f;
				texVAlignOffset = 0.0f;
				
				texness = new Texness();
				
				colmod = new Vector4(1f, 1f, 1f, 1f);
				
				clcTexVerts = new VertexPCT[4];
			}
			
			protected override void updateMe(ViewTrans vt)
			{
				updateTex(vt);
			}
			
			protected override void drawMe(DeviceContext context, ElemDrawData eddat, PreDrawData pddat)
			{
				drawTex(context, eddat, pddat);
			}
			
			// pretty much wrote copy from Barembs
			void updateTex(ViewTrans vt)
			{
				float left = vt.xToScreen(clcRect.Left);
				float right = vt.xToScreen(clcRect.Right);
				float top = vt.yToScreen(clcRect.Top);
				float bottom = vt.yToScreen(clcRect.Bottom);
		
				float tleft = 0f;
				float tright = 0f;
				float ttop = 0f;
				float tbottom = 0f;
		
				if (texMode == TexMode.Fit)
				{
					// skip to answers
					tleft = 0;
					tright = 1;
					ttop = 0;
					tbottom = 1;
				}
				else if (texMode == TexMode.Flat || texMode == TexMode.Zoom)
				{
					float bw = right - left;
					float bh = top - bottom;
					float tw = vt.wToScreen(texW) * texScaleX;
					float th = vt.hToScreen(texH) * texScaleY;
					float sw = tw / bw; // suitably scaled
					float sh = th / bh;
		
					float hao;
					float vao;
					if ((texAlign & TexAlign.OffsetInset) > 0)
					{ // 0 - 1 is like left - right or top - bottom
						hao = texHAlignOffset * (1.0f - sw);
						vao = texVAlignOffset * (1.0f - sh);
					}
					else
					{ // 0 - 1 is like left - right or top - bottom from the topleft corner
						hao = texHAlignOffset;
						vao = texVAlignOffset;
					}
		
					TexAlign tah = texAlign & TexAlign.Horizontal;
					TexAlign tav = texAlign & TexAlign.Verticle;
		
					// zoomness
					if (texMode == TexMode.Zoom)
					{
						if (sw > sh)
						{
							sw = 1.0f;
							sh /= sw;
						}
						else
						{
							sw /= sh;
							sh = 1.0f;
						}
					}
		
					switch (tah)
					{
						case TexAlign.Fillh:
							tleft = 0;
							tright = 1;
							break;
						case TexAlign.Left:
							tleft = 0;
							tright = sw;
							break;
						case TexAlign.Right:
							tleft = 1.0f - sw;
							tright = 1;
							break;
						case TexAlign.Center:
							tleft = 0.5f - sw * 0.5f;
							tright = 0.5f + sw * 0.5f;
							break;
					}
		
					switch (tav)
					{
						case TexAlign.Fillv:
							ttop = 0;
							tbottom = 1;
							break;
						case TexAlign.Top:
							ttop = 0;
							tbottom = sh;
							break;
						case TexAlign.Bottom:
							ttop = 1.0f - sh;
							tbottom = 1;
							break;
						case TexAlign.Middle:
							ttop = 0.5f - sh * 0.5f;
							tbottom = 0.5f + sh * 0.5f;
							break;
					}
		
					// tcoords describe where the image should be, need to transform
					
					float tsx = 1.0f / (tright - tleft);
					float tsy = 1.0f / (tbottom - ttop);
		
					tleft += hao;
					tright += hao;
					ttop += vao;
					tbottom += vao;
		
					tleft = 0 - tleft * tsx;
					tright = 1.0f + (1.0f - tright) * tsx;
					ttop = 0 - ttop * tsy;
					tbottom = 1.0f + (1.0f - tbottom) * tsy;
				}
		
				clcTexVerts[0] = new VertexPCT(new VertexPC(left, top, 0, 1, 1, 1, -1), tleft, ttop); // negative tti means ignore tti
				clcTexVerts[1] = new VertexPCT(new VertexPC(right, top, 0, 1, 1, 1, -1), tright, ttop);
				clcTexVerts[2] = new VertexPCT(new VertexPC(left, bottom, 0, 1, 1, 1, -1), tleft, tbottom);
				clcTexVerts[3] = new VertexPCT(new VertexPC(right, bottom, 0, 1, 1, 1, -1), tright, tbottom);
		
				clcUseTexData = false; // default, may change below
				/*if (texAlign & TXA_pixelOffset)
				{
					if (texW == -1 || texH == -1)
					{
						// fix offsetness - this might need revising (currently does the job for a full screen texture, but not much else)
						clcTexData = D3DXVECTOR4(0.5 / (float)(rect.right - rect.left + 1), 0.5 / (float)(rect.bottom - rect.top + 1), 1.0 / (float)vt->bbuffWidth, 1.0 / (float)vt->bbuffHeight);
						clcUseTexData = true;
						
						for (int i = 0; i < 4; i++) // do ahead of shader
						{
							clcTexVerts[i].tu += clcTexData.x;
							clcTexVerts[i].tv += clcTexData.y;
						}
						// end of stuff that might need revising
					}
					else
					{
						// fix offsetness - this might need revising (currently does the job for a full screen texture, but not much else)
						clcTexData = new Vector4(0.5 / texW, 0.5 / texH, 1.0 / (float)vt.bbuffWidth, 1.0 / (float)vt.bbuffHeight);
						clcUseTexData = true;
						
						for (int i = 0; i < 4; i++) // do ahead of shader
						{
							clcTexVerts[i].tu += clcTexData.X;
							clcTexVerts[i].tv += clcTexData.Y;
						}
						// end of stuff that might need revising
					}
				}*/
			}
			
			// pretty much wrote copied from Barembs - needs beating about a bit
			void drawTex(DeviceContext context, ElemDrawData eddat, PreDrawData pddat)
			{
				if (tech == null || clcTexVerts == null)
					return;
		
				texness.applyTextures(context);
				context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
				
				// eddat thing
				eddat.sectionBuffer.data.colMod = colmod;
				
				eddat.sectionBuffer.update(context);
				eddat.sectionBuffer.applyPStage(context);
				eddat.sectionBuffer.applyVStage(context);
				
				DataStream dstream;
				context.MapSubresource(eddat.vbuff, MapMode.WriteDiscard, MapFlags.None, out dstream);
				
				dstream.WriteRange(clcTexVerts);
				
				dstream.Close();
				
				dstream.Dispose();
				context.UnmapSubresource(eddat.vbuff, 0);
			
				context.InputAssembler.SetVertexBuffers(0, eddat.vbuffBinding);
				//
				
//					effect.setcolMod((float*)&colMod);
//					effect.setViewProj(&idMat);
				// effect.setTicker(ticker); would be nice to have this information
		
//					if (clcUseTexData)
//						effect.setTextureData((float*)&clcTexData.x);
		
				foreach (Pass p in tech.passes)
				{
					p.apply(context);
					
					context.Draw(4, 0);
				}
			}

		}
		
		public class TextElem : AElem
		{
			public string text;
			
			public Vector4 colmod;
			public sdw.Font font;
			
			// clc, ish
			private RectangleF textRect;
			//
			
			public TextElem(string nameN, IElem parentN, Rectangle rectN, sdw.Font fontN, string textN) : base(nameN, parentN, rectN)
			{
				text = textN;

				colmod = new Vector4(1f, 1f, 1f, 1f);
				font = fontN;
			}
			
			protected override void updateMe(ViewTrans vt)
			{
				updateText(vt);
			}
			
			protected override void drawMe(DeviceContext context, ElemDrawData eddat, PreDrawData pddat)
			{
				drawText(context, eddat, pddat);
			}
			
			// pretty much wrote copy from Barembs
			void updateText(ViewTrans vt)
			{
				textRect.Left = vt.xToTextX(clcRect.Left);
				textRect.Right = vt.xToTextX(clcRect.Right);
				textRect.Top = vt.yToTextY(clcRect.Top);
				textRect.Bottom = vt.yToTextY(clcRect.Bottom);
			}
			
			// AAAAAAAAAAAAAA
			void drawText(DeviceContext context, ElemDrawData eddat, PreDrawData pddat)
			{
				
			}
		}
		
		public class Face : ASlide
		{
			
			public int texWidth;
			public int texHeight;
			
			public NamedTexture targetTex;
			public RenderViewPair targetRenderViewPair;
			
			public ConstBuffer<EyeCData> eyeBuffer;
			private ElemDrawData eddat; // this one is a bit weird...
			
			public Color clearColour
			{
				get
				{
					return targetRenderViewPair.clearColour;
				}
				set
				{
					targetRenderViewPair.clearColour = value;
				}
			}
			
			public Face(Device device, string name) : base(name)
			{
				targetRenderViewPair = new UN11.RenderViewPair();
				
				eyeBuffer = new ConstBuffer<EyeCData>(device, EyeCData.defaultSlot);
				eyeBuffer.data.viewProj = Matrix.Identity;
				
				eddat = new ElemDrawData(device);
			}
			
			public void update()
			{
			}
			
			public void draw(DeviceContext context, FaceDrawData fddat, PreDrawData pddat)
			{
				eyeBuffer.update(context);
				eyeBuffer.applyVStage(context);
				
				targetRenderViewPair.apply(context, true, true);
				
				foreach (IElem e in fddat.elems)
				{
					e.draw(context, eddat, pddat);
				}
			}
			
			/// <summary>
			/// Must be called before you try and init anything else
			/// </summary>
			public void setDimension(int texWidthN, int texHeightN)
			{
				texWidth = texWidthN;
				texHeight = texHeightN;
			}
			
			public void initTarget(Device device, Format format, TextureCollection textures)
			{
				targetTex = createRenderNamedTexture(device, "face_" + name, texWidth, texHeight, format, out targetRenderViewPair.renderView);
				textures.Set(targetTex);
			}
			
			public void initTarget(RenderTargetView targetRenderViewN)
			{
				targetRenderViewPair.renderView = targetRenderViewN;
			}
		}
		
		public class OverDrawData : SlideDrawData
		{
			public Over over;
			
			public OverDrawData(Over overN)
			{
				over = overN;
			}
			
			public override void drawSlide(DeviceContext context, PreDrawData pddat)
			{
				over.drawOver(context, this, pddat);
			}
		}
		
		public class Over : Slide, Named
		{
			public string name {get; private set;}
			
			public Texness texness;
			
			public int texWidth;
			public int texHeight;
			
			public NamedTexture targetTex;
			public Texture2D targetStencilTex;
			public RenderViewPair targetRenderViewPair;
			
			public Color clearColour
			{
				get
				{
					return targetRenderViewPair.clearColour;
				}
				set
				{
					targetRenderViewPair.clearColour = value;
				}
			}
			
			public Technique tech;
			
			private Overness overness;
			
			public Over(Device device, string nameN)
			{
				name = nameN;
				targetRenderViewPair = new UN11.RenderViewPair();
				texness = new Texness();
			}
			
			public void drawOver(DeviceContext context, OverDrawData oddat, PreDrawData pddat)
			{
				targetRenderViewPair.apply(context, true, true);
				texness.applyTextures(context);
				
				// TODO: add alphaModes for overs
				pddat.uneleven.blendStates.none.apply(context); // or something like this
				pddat.uneleven.depthStencilStates.zNone.apply(context);
				pddat.uneleven.rasterizerStates.noBackcull.apply(context);
				
				overness.apply(context);
				foreach (Pass p in tech.passes)
				{
					p.apply(context);
					overness.drawOver(context);
				}
			}
			
			public void initOverness(Device device)
			{
				overness = new Overness(texWidth, texHeight);
				overness.build(device);
			}
			
			/// <summary>
			/// Must be called before you try and init anything else
			/// </summary>
			public void setDimension(int texWidthN, int texHeightN)
			{
				texWidth = texWidthN;
				texHeight = texHeightN;
			}
			
			public void initTarget(Device device, Format format, TextureCollection textures)
			{
				targetTex = createRenderNamedTexture(device, "over_" + name, texWidth, texHeight, format, out targetRenderViewPair.renderView);
				textures.Set(targetTex);
			}
			
			public void initTarget(RenderTargetView targetRenderViewN)
			{
				targetRenderViewPair.renderView = targetRenderViewN;
			}
			
			public void initTargetStencil(Device device)
			{
				createStencilBuffer(device, texWidth, texHeight, out targetStencilTex, out targetRenderViewPair.stencilView);
			}
			
			public void initTargetStencil(DepthStencilView targetStencilViewN)
			{
				targetRenderViewPair.stencilView = targetStencilViewN;
			}
		}
		
		public class ViewDrawData : SlideDrawData
		{
			public View view;
			public GeometryDrawDataList geometryDrawDatas = new GeometryDrawDataList();
			public LightList lights = new LightList();
			
			public ViewDrawData(View viewN)
			{
				view = viewN;
			}
			
			public override void drawSlide(DeviceContext context, PreDrawData pddat)
			{
				view.drawView(context, this, pddat);
			}
		}
		
		public class View : ASlide
		{
			public int texWidth;
			public int texHeight;
			
			public ViewMode viewMode;
			
			public float projectionNear;
			public float projectionFar;
			public float dimX;
			public float dimY;
			
			public float3 camPos;
			public float3 camDir;
			public float3 camUp;
			
			public AppliableViewport vp;
			
			public NamedTexture targetTex;
			public Texture2D targetStencilTex;
			public RenderViewPair targetRenderViewPair;
			
			public NamedTexture sideTex;
			public Texture2D sideStencilTex;
			public RenderViewPair sideRenderViewPair;
			
			public Color clearColour
			{
				get
				{
					return targetRenderViewPair.clearColour;
				}
				set
				{
					targetRenderViewPair.clearColour = value;
				}
			}
			
			// clipping
			
			public Matrix viewMat;
			public Matrix projMat;
			public NamedMatrix viewProjVP;
			public NamedMatrix viewProjTex;
			
			private ConstBuffer<EyeCData> eyeBuffer;
			private ConstBuffer<TargetCData> targetBuffer;
			private Overness overness;
			
			public View(Device device, string name, MatrixCollection matrices) : base(name)
			{
				// loads of defaults
				camPos = new Vector3(0f, 0f, 0f);
				camDir = new Vector3(1f, 0f, 0f);
				camUp = new Vector3(0f, 1f, 0f);

				targetRenderViewPair = new UN11.RenderViewPair();
				sideRenderViewPair = new UN11.RenderViewPair();
				
				clearColour = Color.Black;
				
				matrices.Set(viewProjVP = new NamedMatrix("view_" + name + "_viewproj"));
				matrices.Set(viewProjTex = new NamedMatrix("view_" + name + "_viewprojtex"));
				
				eyeBuffer = new ConstBuffer<EyeCData>(device, EyeCData.defaultSlot);
				targetBuffer = new ConstBuffer<TargetCData>(device, TargetCData.defaultSlot);
			}
			
			public void drawView(DeviceContext context, ViewDrawData vddat, PreDrawData pddat)
			{
				// don't forget to update!
				apply(context);
				
				DrawData ddat = new DrawData(pddat, SceneType.Colour);
				ddat.eyeBuffer = eyeBuffer;
				ddat.targetBuffer = targetBuffer;
				ddat.lightMapBuffer = null; // no light maps here
				ddat.targetTex = targetTex;
				ddat.targetRenderViewPair = targetRenderViewPair;
				ddat.sideTex = sideTex;
				ddat.sideRenderViewPair = sideRenderViewPair;
				ddat.overness = overness;
				ddat.vp = vp;
				ddat.lights = vddat.lights;
				ddat.viewProjVP = viewProjVP.mat;
				
				targetRenderViewPair.apply(context, true, true);
				
				foreach (GeometryDrawData gddat in vddat.geometryDrawDatas)
				{
					gddat.drawGeometry(context, ddat);
				}
			}
			
			public void initOverness(Device device)
			{
				overness = new Overness(texWidth, texHeight);
				overness.build(device);
			}
			
			public void setProj(ViewMode viewModeN, float dimXN, float dimYN, float near, float far)
			{
				viewMode = viewModeN;
				dimX = dimXN;
				dimY = dimYN;
				projectionNear = near;
				projectionFar = far;
			}
			
			/// <summary>
			/// Must be called before you try and init anything else
			/// </summary>
			public void setDimension(int texWidthN, int texHeightN)
			{
				texWidth = texWidthN;
				texHeight = texHeightN;
				
				vp = new AppliableViewport(0, 0, texWidth, texHeight, 0.0f, 1.0f);
				updateTargetCData();
			}
			
			public void initTarget(Device device, Format format, TextureCollection textures)
			{
				targetTex = createRenderNamedTexture(device, "view_" + name, texWidth, texHeight, format, out targetRenderViewPair.renderView);
				textures.Set(targetTex);
			}
			
			public void initTarget(RenderTargetView targetRenderViewN)
			{
				targetRenderViewPair.renderView = targetRenderViewN;
			}
			
			public void initSide(Device device, Format format, TextureCollection textures)
			{
				sideTex = createRenderNamedTexture(device, "view_" + name + "_side", texWidth, texHeight, format, out sideRenderViewPair.renderView);
				textures.Set(sideTex);
			}
			
			public void initSide(RenderTargetView sideRenderViewN)
			{
				sideRenderViewPair.renderView = sideRenderViewN;
			}
			
			public void initTargetStencil(Device device)
			{
				createStencilBuffer(device, texWidth, texHeight, out targetStencilTex, out targetRenderViewPair.stencilView);
			}
			
			public void initTargetStencil(DepthStencilView targetStencilViewN)
			{
				targetRenderViewPair.stencilView = targetStencilViewN;
			}
			
			public void initSideStencil(Device device)
			{
				createStencilBuffer(device, texWidth, texHeight, out sideStencilTex, out sideRenderViewPair.stencilView);
			}
			
			public void initSideStencil(DepthStencilView sideStencilViewN)
			{
				sideRenderViewPair.stencilView = sideStencilViewN;
			}
			
			private void updateEyeCData()
			{
				matrix.Transpose(ref viewProjVP.mat, out eyeBuffer.data.viewProj);
				eyeBuffer.data.eyePos = new Vector4(camPos, 1.0f);
				eyeBuffer.data.eyeDir = new Vector4(camDir, 1.0f);
				eyeBuffer.data.farDepth = projectionFar;
				eyeBuffer.data.invFarDepth = 1.0f / projectionFar;
			}
			
			private void updateTargetCData()
			{
				Vector4 texData = new Vector4(0.5f / texWidth, 0.5f / texHeight, 1.0f / texWidth, 1.0f / texHeight);
				targetBuffer.data.texData = texData;
				
				float[] matDat = new float[16]{
					(float)vp.vp.Width / 2.0f, 0.0f, 0.0f, 0.0f,
					0.0f, -(float)vp.vp.Height / 2.0f, 0.0f, 0.0f,
					0.0f, 0.0f, vp.vp.MaxDepth - vp.vp.MinDepth, 0.0f,
					vp.vp.X + (float)vp.vp.Width / 2.0f, vp.vp.Y + (float)vp.vp.Height / 2.0f, vp.vp.MinDepth, 1.0f
				};
				
				targetBuffer.data.targetVPMat = new Matrix(matDat);
			}
			
			private void updateMats()
			{
				Vector3 eyeVec = camPos;
				Vector3 targVec = Vector3.Add(eyeVec, camDir);
				Vector3 upVec = camUp;
				
				Matrix.LookAtLH(ref eyeVec, ref targVec, ref upVec, out viewMat);
				if (viewMode == ViewMode.Persp)
					matrix.PerspectiveFovLH(dimX, dimY, projectionNear, projectionFar, out projMat);
				else if (viewMode == ViewMode.Ortho)
					matrix.OrthoLH(dimX, dimY, projectionNear, projectionFar, out projMat);
				matrix.Multiply(ref viewMat, ref projMat, out viewProjVP.mat);
				viewProjTex.mat = viewProjVP.mat;
				
				texAlignViewProj(ref viewProjTex.mat);
			}
			
			public void update()
			{
				updateMats();
				updateEyeCData(); // must be done after mats
				updateTargetCData();
			}
			
			// so stuff below can be lazy
			public void apply(DeviceContext context)
			{
				vp.apply(context);
				eyeBuffer.applyVStage(context);
				eyeBuffer.update(context);
			}

			public void dirNormalAt(Vector3 camTarg)
			{
				camDir = Vector3.Normalize(Vector3.Subtract(camTarg, camPos));
			}
		}
		
		// heavily based off view
		public class LightDrawData : SlideDrawData
		{
			public Light light;
			public GeometryDrawDataList geometryDrawDatas = new GeometryDrawDataList();
			
			public LightDrawData(Light lightN)
			{
				light = lightN;
			}
			
			public override void drawSlide(DeviceContext context, PreDrawData pddat)
			{
				light.drawLight(context, this, pddat);
			}
		}
		
		// heavily based off view
		public class Light : ASlide
		{
			public int texWidth;
			public int texHeight;
			
			public ViewMode viewMode;
			public LightType lightType;
			
			public float projectionNear;
			public float projectionFar;
			public float dimX;
			public float dimY;
			public float lightDepth;
			
			public bool lightEnabled;
			
			public float3 lightPos;
			public float3 lightDir;
			public float3 lightUp;
			public float4 lightAmbient;
			public float4 lightColMod;
			
			public AppliableViewport vp;
			
			public NamedTexture targetTex;
			public Texture2D targetStencilTex;
			public RenderViewPair targetRenderViewPair;
			
			// clipping
			
			public Matrix viewMat;
			public Matrix projMat;
			public NamedMatrix viewProjVP;
			public NamedMatrix viewProjTex;
			
			public ConstBuffer<LightCData> lightBuffer;
			
			private ConstBuffer<EyeCData> eyeBuffer;
			private ConstBuffer<LightMapCData> lightMapBuffer;
			
			public bool useLightMap;
			
			public Light(Device device, string name, MatrixCollection matrices) : base(name)
			{
				// loads of defaults
				lightPos = new Vector3(0f, 0f, 0f);
				lightDir = new Vector3(1f, 0f, 0f);
				lightUp = new Vector3(0f, 1f, 0f);
				
				lightEnabled = true;
				
				lightType = LightType.Point;
				
				matrices.Set(viewProjVP = new NamedMatrix("view_" + name + "_viewproj"));
				matrices.Set(viewProjTex = new NamedMatrix("view_" + name + "_viewprojtex"));
				
				lightBuffer = new ConstBuffer<LightCData>(device, LightCData.defaultSlot);
				
				eyeBuffer = new ConstBuffer<EyeCData>(device, EyeCData.defaultSlot);
				lightMapBuffer = new ConstBuffer<LightMapCData>(device, LightMapCData.defaultSlot);
			}
			
			public void drawLight(DeviceContext context, LightDrawData lddat, PreDrawData pddat)
			{
				// don't forget to update!
				apply(context);
				
				DrawData ddat = new DrawData(pddat, SceneType.Light);
				ddat.eyeBuffer = eyeBuffer;
				ddat.lightMapBuffer = lightMapBuffer;
				ddat.targetTex = targetTex;
				ddat.targetRenderViewPair = targetRenderViewPair;
				ddat.vp = vp;
				
				//pddat.uneleven.depthStencilStates.zReadWrite.
				
				foreach (GeometryDrawData gddat in lddat.geometryDrawDatas)
				{
					gddat.drawGeometry(context, ddat);
				}
			}
			
			public void setProj(ViewMode viewModeN, float dimXN, float dimYN, float near, float far)
			{
				viewMode = viewModeN;
				dimX = dimXN;
				dimY = dimYN;
				projectionNear = near;
				projectionFar = far;
			}
			
			/// <summary>
			/// Must be called before you try and init anything else
			/// </summary>
			public void setDimension(int texWidthN, int texHeightN)
			{
				texWidth = texWidthN;
				texHeight = texHeightN;
				
				vp = new AppliableViewport(0, 0, texWidth, texHeight, 0.0f, 1.0f);
			}
			
			public void initTarget(Device device, Format format, TextureCollection textures)
			{
				targetTex = createRenderNamedTexture(device, "view_" + name, texWidth, texHeight, format, out targetRenderViewPair.renderView);
				textures.Set(targetTex);
			}
			
			public void initTarget(RenderTargetView targetRenderViewN)
			{
				targetRenderViewPair.renderView = targetRenderViewN;
			}
			
			public void initTargetStencil(Device device)
			{
				createStencilBuffer(device, texWidth, texHeight, out targetStencilTex, out targetRenderViewPair.stencilView);
			}
			
			public void initTargetStencil(DepthStencilView targetStencilViewN)
			{
				targetRenderViewPair.stencilView = targetStencilViewN;
			}

			public void updateLightCData()
			{
				if (useLightMap)
				{
					matrix.Transpose(ref viewProjTex.mat, out lightBuffer.data.lightViewProj); // texAligned
				}
				
				lightBuffer.data.lightPos = new Vector4(lightPos, 1.0f);
				lightBuffer.data.lightDir = new Vector4(lightDir, 1.0f);
				lightBuffer.data.lightAmbient = lightAmbient;
				lightBuffer.data.lightColMod = lightColMod;
				lightBuffer.data.lightDepth = lightDepth;
				lightBuffer.data.lightCoof = 1.0f; // HACK: are we removing this?
				lightBuffer.data.lightType = (float)lightType;
			}
			
			private void updateEyeCData()
			{
				matrix.Transpose(ref viewProjVP.mat, out eyeBuffer.data.viewProj);
				//eyeBuffer.data.eyePos = new Vector4(camPos, 1.0f);
				//eyeBuffer.data.eyeDir = new Vector4(camDir, 1.0f);
				//eyeBuffer.data.farDepth = projectionFar;
				//eyeBuffer.data.invFarDepth = 1.0f / projectionFar;
			}
			
			private void updateLightMapCData()
			{
				// errhm
			}
			
			private void updateMats()
			{
				Vector3 eyeVec = lightPos;
				Vector3 targVec = Vector3.Add(eyeVec, lightDir);
				Vector3 upVec = lightUp;
				
				Matrix.LookAtLH(ref eyeVec, ref targVec, ref upVec, out viewMat);
				if (viewMode == ViewMode.Persp)
					matrix.PerspectiveFovLH(dimX, dimY, projectionNear, projectionFar, out projMat);
				else if (viewMode == ViewMode.Ortho)
					matrix.OrthoLH(dimX, dimY, projectionNear, projectionFar, out projMat);
				matrix.Multiply(ref viewMat, ref projMat, out viewProjVP.mat);
				viewProjTex.mat = viewProjVP.mat;
				
				texAlignViewProj(ref viewProjTex.mat);
			}
			
			public void update()
			{
				updateMats();
				updateEyeCData(); // must be done after mats
				updateLightCData();
				updateLightMapCData();
			}
			
			public void apply(DeviceContext context)
			{
				vp.apply(context);
				eyeBuffer.update(context);
				eyeBuffer.applyVStage(context);
				lightMapBuffer.update(context);
				lightMapBuffer.applyVStage(context);
			}
			
			public void setRenderTarget(DeviceContext context, bool clearDepth, bool clearColor)
			{
				targetRenderViewPair.apply(context, clearDepth, clearColor);
			}

			public void dirNormalAt(Vector3 camTarg)
			{
				lightDir = Vector3.Normalize(Vector3.Subtract(camTarg, lightPos));
			}
		}
		
		public class LightList : List<Light>
		{
		}
		
		// stuff for external stuff to throw at internal stuff
		public class FrameDrawData
		{
			public SlideDrawDataList slideDrawDatas = new UN11.SlideDrawDataList();
		}
		
		// UN11ness
		public ShaderBytecodeCollection bytecodes = new ShaderBytecodeCollection();
		public TechniqueCollection techniques = new TechniqueCollection();
		public MatrixCollection matrices = new MatrixCollection();
		public TextureCollection textures = new TextureCollection();
		
		public ModelCollection models = new ModelCollection();
		public SlideList slides = new SlideList();
		
		public BlendStates blendStates;
		public DepthStencilStates depthStencilStates;
		public RasterizerStates rasterizerStates;
		public SamplerStates samplerStates;
		
		public EventManager eventManager = new UN11.EventManager();
		
		public int frames {get; private set;}
		
		public Timing timing;
		private Timing.Span frameSpan;
		public long lastFrameSpan {get; private set;}
		
		public Device device {get; private set;}
		
		private void applySamplers(DeviceContext context)
		{
			samplerStates.linearWrap.Apply(context, (int)SamplerSlot.linearWrap);
			samplerStates.pointWrap.Apply(context, (int)SamplerSlot.pointWrap);
			samplerStates.linearBorder.Apply(context, (int)SamplerSlot.linearBorder);
			samplerStates.pointBorder.Apply(context, (int)SamplerSlot.pointBorder);
		}
		
		private void tickTime()
		{
			frames++;
			lastFrameSpan = frameSpan.ellapsed;
			frameSpan.accumulate();
		}
		
		private void handleEvents()
		{
			// queue tick
			eventManager.queueEvent(new TickEvent(timing.curTime));
			
			
		}
		
		public void tick()
		{
			tickTime();
			handleEvents();
		}
		
		public void drawFrame(DeviceContext context, FrameDrawData fddat)
		{
			// some timing stuff
			
			PreDrawData pddat = new PreDrawData(this);
			
			applySamplers(context); // ah, the all important samplers...
			
			foreach (SlideDrawData sddat in fddat.slideDrawDatas)
			{
				sddat.drawSlide(context, pddat);
			}
		}
		
		public UN11(Device deviceN)
		{
			device = deviceN;
			
			blendStates = new BlendStates(device);
			depthStencilStates = new DepthStencilStates(device);
			rasterizerStates = new RasterizerStates(device);
			samplerStates = new SamplerStates(device);
			
			timing = new Timing();
			frameSpan = timing.newSpan("frameSpan");
		}
		
		public void disposeAll()
		{
			bytecodes.disposeAll();
		}
		
		public ShaderBytecodeDesc loadShaderBytecode(string fileName, string shaderName, string shaderFlags)
		{
			return bytecodes.loadShaderBytecode(fileName, shaderName, shaderFlags);
		}
		
		public Technique createTechnique(string name, VertexType vertexTypeN, ShaderBytecodeDesc vshadeDescN, ShaderBytecodeDesc pshadeDescN)
		{
			return createTechnique(name, vertexTypeN, new ShaderBytecodeDesc[] { vshadeDescN }, new ShaderBytecodeDesc[] { pshadeDescN });
		}
		
		public class FileParsingException : Exception
		{
			public string src {get; private set;}
			public string file {get; private set;}
			public int lnum {get; private set;}
			public string line {get; private set;}
			public string msg {get; private set;}
			
			public FileParsingException(string srcN, string fileN, int lnumN, string lineN, string msgN) : base(msgN + " in " + fileN + " at line " + lnumN + " (\"" + lineN + "\")" + " from " + srcN)
			{
				src = srcN;
				file = fileN;
				lnum = lnumN;
				line = lineN;
				msg = msgN;
			}
			
			public override string ToString()
			{
				return base.Message;
			}
		}
		
		public delegate void FileParsingExceptionThrower(string msg);
		
		public void loadTechniquesFromFile(string fileName)
		{
			VertexType vertexType = VertexType.VertexPC;
			
			string curName = null;
			string shaderFileName = null;
			List<ShaderBytecodeDesc> psList = new List<ShaderBytecodeDesc>();
			List<ShaderBytecodeDesc> vsList = new List<ShaderBytecodeDesc>();
			
			using (System.IO.StreamReader reader = new System.IO.StreamReader(fileName))
			{
				int lnum = 0;
				string line = "";
				
				FileParsingExceptionThrower throwFPE = (msg) =>
				{
					throw new FileParsingException("loadTechniquesFromFile", fileName, lnum, line, msg);
				};
				
				while (!reader.EndOfStream)
				{
					lnum++;
					line = reader.ReadLine();
					
					int ci = line.IndexOf("//");
					if (ci != -1)
						line.Substring(0, ci);
					line = line.Trim();
					if (line == "")
						continue;
					
					string[] data = line.Split(' ');
					if (data.Length > 0)
					{
						if (data[0] == "end")
						{
							if (data[1] == "tech")
							{
								createTechnique(curName, vertexType, vsList.ToArray(), psList.ToArray());
								vsList.Clear();
								psList.Clear();
							}
						}
						else if (data[0] == "tech")
						{
							if (data.Length < 2)
								throwFPE("Missing argument after \"tech\" - expected the technique name");
							
							curName = data[1];
						}
						else if (data[0] == "pass")
						{
							if (data.Length < 4)
								throwFPE("Missing argument after \"pass\" - expected some of form \"pass <vs_version> <vs_name> <ps_version> <ps_name>\"");
							
							psList.Add(new ShaderBytecodeDesc(shaderFileName, data[4], data[3]));
							vsList.Add(new ShaderBytecodeDesc(shaderFileName, data[2], data[1]));
						}
						else if (data[0] == "shaderfile")
						{
							shaderFileName = line.Substring(11);
						}
						else if (data[0] == "vertex")
						{
							if (data.Length < 2)
								throwFPE("Missing argument after \"vertex\" - expected a vertex type");
							
							if (data[1] == "PC")
							{
								vertexType = VertexType.VertexPC;
							}
							else if (data[1] == "PCT")
							{
								vertexType = VertexType.VertexPCT;
							}
							else if (data[1] == "over")
							{
								vertexType = VertexType.VertexOver;
							}
							else
								throwFPE("Unrecognised vertex type \"" + data[1] + "\"");
						}
					}
				}
			}
		}
		
		public class strPair
		{
			public string gin;
			public string rpl;
			
			public strPair(string ginN, string rplN)
			{
				gin = ginN;
				rpl = rplN;
			}
		}

		
		public class iOff
		{
			public string name;
			public int i;
			
			public iOff(string nameN, int iN)
			{
				name = nameN;
				i = iN;
			}
		}
		
		public class lpTti
		{
			public string segName;
			public int index;
			
			public lpTti(string segNameN, int indexN)
			{
				segName = segNameN;
				index = indexN;
			}
		}

		public short stoIndex(string str, List<iOff> iOffs)
		{
			string[] data = str.Split('+');
			if (data.Length > 1)
			{
				int f = 0;
				for (int i = iOffs.Count - 1; i >= 0; i--)
				{
					if (data[0] == iOffs[i].name)
					{
						f = iOffs[i].i;
						break;
					}
				}
				f += int.Parse(data[1]);
				return (short)f;
			}
			return short.Parse(str);
		}
		
		public int loadModelsFromFile(String fileName, DeviceContext context)
		{
			int count = 0;
			
			List<strPair> reps = new List<strPair>();
			List<iOff> iOffs = new List<iOff>();
			List<lpTti> latePlaceTtis = new List<lpTti>();
			
			Dictionary<string, SceneType> sceneTypes = new Dictionary<string, SceneType>();
			sceneTypes.Add("default", SceneType.Colour);
			sceneTypes.Add("colour", SceneType.Colour);
			sceneTypes.Add("light", SceneType.Light);
			for (int i = 0; i < (int)SceneType.Length; i++)
				sceneTypes.Add(i.ToString(), (SceneType)i);
			
			Model curModel = null;
			Segment lastSegment = null; // if null, means curSeg should be added to the model
			Segment curSegment = null;
			Section curSection = null;
			
			SceneType curSceneType = SceneType.Length; // invalid value
			Prettyness curPrettyness = null;
			Texness curTexness = null;
			
			List<VertexPC> vPCs = new List<VertexPC>();
			List<VertexPCT> vPCTs = new List<VertexPCT>();
			List<short> indices = new List<short>();
			
			int curTti = -1;
			int nextTti = 0;
			bool manualNormals = false;
			bool subOffset = false;
			
			VertexType vertexType = VertexType.VertexPC;
			
			using (System.IO.StreamReader reader = new System.IO.StreamReader(fileName))
			{
				int lnum = 0;
				string line = "";
				
				FileParsingExceptionThrower throwFPE = (msg) =>
				{
					throw new FileParsingException("loadTechniquesFromFile", fileName, lnum, line, msg);
				};
				
				while (!reader.EndOfStream)
				{
					lnum++;
					line = reader.ReadLine();
					
					int ci = line.IndexOf("//");
					if (ci != -1)
						line.Substring(0, ci);
					line = line.Trim();
					if (line == "")
						continue;
					
					if (line.StartsWith("rep "))
					{
					}
					else
					{
						foreach (strPair sp in reps)
						{
							line = line.Replace(sp.gin, sp.rpl);
						}
					}
					
					string[] data = line.Split(' ');
					
					if (data.Length > 0)
					{
						if (data[0] == "end")
						{
							if (data[1] == "mdl")
							{
								// find highTti pass batchcopies to all section
								curModel.highTti = 0;
								foreach (Segment seg in curModel.allSegs)
								{
									if (seg.transIndex > curModel.highTti)
									{
										curModel.highTti = seg.transIndex;
										
										foreach (Blend blnd in seg.blends)
										{
											if (blnd.transIndex > curModel.highTti)
											{
												curModel.highTti = blnd.transIndex;
											}
										}
									}
								}
								foreach (Section sec in curModel.sections)
								{
									sec.batchCopies = curModel.batchCopies;
								}
								
								// sort out verts
								if (vertexType == VertexType.VertexPC)
								{
									for (int i = latePlaceTtis.Count - 1; i >= 0; i--)
									{
										lpTti lpt = latePlaceTtis[i];
										VertexPC ov = vPCs[lpt.index];
										ov.tti = curModel.getSegTti(lpt.segName);
										vPCs[lpt.index] = ov;
									}
									
									// TODO: IMPLEMENT/PORT ME!!
									//if (!manualNormals)
									//	autoGenNormals((void*)&vPCs.front(), (short*)&indices.front(), vPCs.size(), indices.size(), VX_PC, nrmVis);
									
									curModel.numVertices = vPCs.Count;
									curModel.createVBuff(device, context, vPCs.ToArray(), null);
								}
								else if (vertexType == VertexType.VertexPCT)
								{
									for (int i = latePlaceTtis.Count - 1; i >= 0; i--)
									{
										lpTti lpt = latePlaceTtis[i];
										VertexPCT ov = vPCTs[lpt.index];
										ov.tti = curModel.getSegTti(lpt.segName);
										vPCTs[lpt.index] = ov;
									}
									
									// TODO: IMPLEMENT/PORT ME!!
									//if (!manualNormals)
									//	autoGenNormals((void*)&vPCTs.fron"t(), (short*)&indices.front(), vPCTs.size(), indices.size(), VX_PCT, nrmVis);
									
									curModel.numVertices = vPCTs.Count;
									curModel.createVBuff(device, context, null, vPCTs.ToArray());
								}
								
								curModel.numIndices = indices.Count;
								curModel.createIBuff(device, context, indices.ToArray());
								
								// clearnup
								vPCs.Clear();
								vPCTs.Clear();
								indices.Clear();
								latePlaceTtis.Clear();
								
								// finish up
								curModel.transArr = new TransArr();
								curModel.transArrBuffer = new TransArrBuffer(device);
								curModel.transArr.create(curModel.highTti + 1);
								curModel.createSegmentBoxes();
								models.Add(curModel);
								
								// TODO: remove this is nothing has broken
//								foreach (Section ss in curModel.sections)
//								{
//									ss.indexCount = curModel.numVertices;
//								}
								
								iOffs.Clear();
								lastSegment = null;
								curSegment = null;
								manualNormals = false;
								subOffset = false;
							}
							else if (data[1] == "blend")
							{
								curTti = curSegment.transIndex;
							}
							else if (data[1] == "seg")
							{
								lastSegment = curSegment.parent;
								curSegment = lastSegment;
								if (curSegment != null)
									curTti = curSegment.transIndex;
								else
									curTti = -1; // useful to check for vertices which arn't actually latePlaced
							}
							else if (data[1] == "sec")
							{
								curSection.triCount = (indices.Count - curSection.indexOffset) / 3;
							}
						}
						else if (data[0] == "mdl")
						{
							curModel = new Model(data[1]);
							nextTti = 0;
							curModel.batchCopies = 1;
						}
						else if (data[0] == "blend")
						{
							curTti = nextTti;
							nextTti++;
							curSegment.addBlend(data[1], curTti, float.Parse(data[2]));
						}
						else if (data[0] == "seg")
						{
							foreach (Segment ts in curModel.allSegs)
							{
								if (ts.name == data[1])
								{
									curSegment = ts;
									lastSegment = curSegment; // hmm?
									curTti = curSegment.transIndex;
									goto oldSeg;
								}
							}
							
							curSegment = new Segment(data[1]);
							curTti = nextTti;
							nextTti++;
							curSegment.transIndex = curTti;
							
							if (lastSegment == null)
							{
								curModel.segments.Add(curSegment);
								curSegment.parent = null;
							}
							else
							{
								lastSegment.segments.Add(curSegment);
								curSegment.parent = lastSegment;
							}
							
							curSegment.model = curModel;
							curModel.allSegs.Add(curSegment);
							
						oldSeg:
							continue;
						}
						else if (data[0] == "sec")
						{
							curSection = new Section(device, data[1]);
							
							curSection.sectionEnabled = true;
							
							curSection.drawDecals = true;
							curSection.acceptDecals = true;
							
							curSection.acceptDecals = true;
							
							curSection.indexOffset = indices.Count;
							
							curSection.setVertexType(vertexType);
							
							curModel.sections.Add(curSection);
						}
						else if (data[0] == "batchcopies")
						{
							curModel.batchCopies = int.Parse(data[1]);
						}
						else if (data[0] == "techniques_dx11")
						{
							loadTechniquesFromFile(line.Substring(16));
						}
						else if (data[0] == "mat")
						{
							int idx = int.Parse(data[1]);
							//curSection.mats[idx] = matrices[data[2]];
							// TODO: fix mat
						}
						else if (data[0] == "pretty")
						{
							if (data.Length < 2)
								throwFPE("Missing argument after \"vertex\" - expected a SceneType name");
							
							curSceneType = sceneTypes[data[1]];
							
							curTexness = curSection.prettynessess[(int)curSceneType].texness;
							curPrettyness = curSection.prettynessess[(int)curSceneType].prettyness;
						}
						else if (data[0] == "texture")
						{
							curTexness.tex = createTexture(line.Substring(8));
							curTexness.useTex = true;
						}
						else if (data[0] == "texture0")
						{
							curTexness.tex0 = createTexture(line.Substring(9));
							curTexness.useTex0 = true;
						}
						else if (data[0] == "texture1")
						{
							curTexness.tex1 = createTexture(line.Substring(9));
							curTexness.useTex1 = true;
						}
						else if (data[0] == "texture2")
						{
							curTexness.tex2 = createTexture(line.Substring(9));
							curTexness.useTex2 = true;
						}
						else if (data[0] == "texture3")
						{
							curTexness.tex3 = createTexture(line.Substring(9));
							curTexness.useTex3 = true;
						}
						else if (data[0] == "colmod")
						{
							curPrettyness.colMod = new Vector4(float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]), float.Parse(data[4]));
						}
						else if (data[0] == "alpha")
						{
							if (data[1] == "none")
								curPrettyness.alphaMode = AlphaMode.None;
							else if (data[1] == "nice")
								curPrettyness.alphaMode = AlphaMode.Nice;
							else if (data[1] == "add")
								curPrettyness.alphaMode = AlphaMode.Add;
						}
						else if (data[0] == "decals")
						{
							if (data[1] == "accept")
							{
								curSection.acceptDecals = true;
							}
							else if (data[1] == "noaccept")
							{
								curSection.acceptDecals = false;
							}
							else if (data[1] == "draw")
							{
								curSection.drawDecals = true;
							}
							else if (data[1] == "nodraw")
							{
								curSection.drawDecals = false;
							}
						}
						else if (data[0] == "dyndecals")
						{
							if (data[1] == "draw")
							{
								curSection.drawDynamicDecals = true;
							}
							else if (data[1] == "nodraw")
							{
								curSection.drawDynamicDecals = false;
							}
						}
						else if (data[0] == "lighting")
						{
							if (data[1] == "none")
							{
								curPrettyness.lightingMode = LightingMode.None;
							}
							else if (data[1] == "full")
							{
								curPrettyness.lightingMode = LightingMode.Full;
							}
						}
						else if (data[0] == "manualnormals")
						{
							manualNormals = true;
						}
						else if (data[0] == "suboffset")
						{
							subOffset = true;
						}
						else if (data[0] == "v")
						{
							float vx = float.Parse(data[1]);
							float vy = float.Parse(data[2]);
							float vz = float.Parse(data[3]);
							float vw = float.Parse(data[4]);
							
							if (subOffset)
							{
								vx -= curSegment.or.offset.X;
								vy -= curSegment.or.offset.Y;
								vz -= curSegment.or.offset.Z;
							}
							
							if (manualNormals)
							{
								if (vertexType == VertexType.VertexPC)
									vPCs.Add(new VertexPC(vx, vy, vz, vw, float.Parse(data[5]), float.Parse(data[6]), float.Parse(data[7]), float.Parse(data[8]), float.Parse(data[9]), curTti));
								else if (vertexType == VertexType.VertexPCT)
									vPCTs.Add(new VertexPCT(new VertexPC(vx, vy, vz, vw, float.Parse(data[5]), float.Parse(data[6]), float.Parse(data[7]), float.Parse(data[8]), float.Parse(data[9]), curTti), float.Parse(data[10]), float.Parse(data[11])));
							}
							else
							{
								if (vertexType == VertexType.VertexPC)
									vPCs.Add(new VertexPC(vx, vy, vz, vw, float.Parse(data[5]), float.Parse(data[6]), curTti));
								else if (vertexType == VertexType.VertexPCT)
									vPCTs.Add(new VertexPCT(new VertexPC(vx, vy, vz, vw, float.Parse(data[5]), float.Parse(data[6]), curTti), float.Parse(data[7]), float.Parse(data[8])));
							}
						}
						else if (data[0] == "lpt")
						{
							int ttti = -1;
							if (vertexType == VertexType.VertexPC)
							{
								if ((ttti = curModel.getSegTti(data[1])) != -1)
								{
									VertexPC ov = vPCs[vPCs.Count - 1];
									ov.tti = ttti;
									vPCs[vPCs.Count - 1] = ov;
								}
								else
								{
									latePlaceTtis.Add(new lpTti(data[1], vPCs.Count - 1));
								}
							}
							else if (vertexType == VertexType.VertexPCT)
							{
								if ((ttti = curModel.getSegTti(data[1])) != -1)
								{
									VertexPCT ov = vPCTs[vPCTs.Count - 1];
									ov.tti = ttti;
									vPCTs[vPCTs.Count - 1] = ov;
								}
								else
								{
									latePlaceTtis.Add(new lpTti(data[1], vPCTs.Count - 1));
								}
							}
						}
						else if (data[0] == "f")
						{
							indices.Add(stoIndex(data[1], iOffs));
							indices.Add(stoIndex(data[2], iOffs));
							indices.Add(stoIndex(data[3], iOffs));
						}
						else if (data[0] == "s")
						{
							int surfaceStart = int.Parse(data[1]);
							int colDim = int.Parse(data[2]);
							int numCols = int.Parse(data[3]);
							
							for (int i = 0; i < numCols - 1; i++)
							{
								for (int j = 0; j < colDim - 1; j++)
								{
									indices.Add((short)(surfaceStart + i * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j + 1));
									indices.Add((short)(surfaceStart + i * colDim + j + 1));
									
									indices.Add((short)(surfaceStart + i * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j + 1));
								}
							}
						}
						else if (data[0] == "cs")
						{
							int surfaceStart = int.Parse(data[1]);
							int colDim = int.Parse(data[2]);
							int numCols = int.Parse(data[3]);
							
							for (int i = 0; i < numCols - 1; i++)
							{
								for (int j = 0; j < colDim - 1; j++)
								{
									indices.Add((short)(surfaceStart + i * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j + 1));
									indices.Add((short)(surfaceStart + i * colDim + j + 1));
									
									indices.Add((short)(surfaceStart + i * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j));
									indices.Add((short)(surfaceStart + (i + 1) * colDim + j + 1));
								}
								
								indices.Add((short)(surfaceStart + i * colDim + colDim - 1));
								indices.Add((short)(surfaceStart + (i + 1) * colDim));
								indices.Add((short)(surfaceStart + i * colDim));
								
								indices.Add((short)(surfaceStart + i * colDim + colDim - 1));
								indices.Add((short)(surfaceStart + (i + 1) * colDim + colDim - 1));
								indices.Add((short)(surfaceStart + (i + 1) * colDim));
							}
						}
						else if (data[0] == "offset")
						{
							curSegment.or.offset = new Vector3(float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]));
						}
						else if (data[0] == "rotation")
						{
							curSegment.or.rotation = new Vector3(float.Parse(data[1]), float.Parse(data[2]), float.Parse(data[3]));
						}
						else if (data[0] == "technique")
						{
							curPrettyness.tech = techniques[data[1]];
						}
						else if (data[0] == "technique_lit")
						{
							curPrettyness.litTech = techniques[data[1]];
						}
						else if (data[0] == "technique_light")
						{
							curPrettyness.lightTech = techniques[data[1]];
						}
						else if (data[0] == "technique_decal")
						{
							curPrettyness.decalTech = techniques[data[1]];
						}
						else if (data[0] == "technique_dyndecal")
						{
							curPrettyness.dynamicDecalTech = techniques[data[1]];
						}
						else if (data[0] == "technique_over")
						{
							curPrettyness.overTech = techniques[data[1]];
						}
						else if (data[0] == "vertex")
						{
							if (data.Length < 2)
								throwFPE("Missing argument after \"vertex\" - expected a vertex type");
							
							if (data[1] == "PC")
							{
								vertexType = VertexType.VertexPC;
								curModel.stride = VertexPC.size;
							}
							else if (data[1] == "PCT")
							{
								vertexType = VertexType.VertexPCT;
								curModel.stride = VertexPCT.size;
							}
							else
								throwFPE("Invalid or unrecognised vertex type \"" + data[1] + "\"");
							
							curModel.vertexType = vertexType;
						}
						else if (data[0] == "rep")
						{
							// rem old
							for (int i = reps.Count - 1; i >= 0; i--)
							{
								if (reps[i].gin == data[1])
								{
									reps.RemoveAt(i);
								}
							}
							// add new
							reps.Add(new strPair(data[1], line.Substring(5 + data[1].Length)));
						}
						else if (data[0] == "scenetype")
						{
							sceneTypes.Add(data[1], (SceneType)int.Parse(data[1]));
						}
						else if (data[0] == "ioff")
						{
							// rem old
							for (int i = iOffs.Count - 1; i >= 0; i--)
							{
								if (iOffs[i].name == data[1])
								{
									iOffs.RemoveAt(i);
								}
							}
							// add new
							if (vertexType == VertexType.VertexPC)
								iOffs.Add(new iOff(data[1], (int)vPCs.Count));
							else if (vertexType == VertexType.VertexPCT)
								iOffs.Add(new iOff(data[1], (int)vPCTs.Count));
						}
					}
				}
			}
			
			return count;
		}
		
		public NamedTexture createTexture(string name)
		{
			// TODO: Work out why we can't load my TGAs (seems happy with PNGs)
			try
			{
				
				if (textures.ContainsKey(name))
					return textures[name];
				else
				{
					Texture2D tex = Texture2D.FromFile<Texture2D>(device, name);
					ShaderResourceView texView = new ShaderResourceView(device, tex);
					NamedTexture res = new NamedTexture(name, tex, texView);
					textures.Add(res);
					return res;
				}
				
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error loading texture \"" + name + "\"");
				Console.WriteLine(ex.Message);
				return null;
			}
		}
		
		public Technique createTechnique(string name, VertexType vertexTypeN, ShaderBytecodeDesc[] vshadeDescN, ShaderBytecodeDesc[] pshadeDescN)
		{
			Technique temp = new Technique(name);
			for (int i = 0; i < vshadeDescN.Length; i++)
			{
				Pass p = new Pass(device, vertexTypeN, vshadeDescN[i], pshadeDescN[i], bytecodes);
				temp.passes.Add(p);
			}
			techniques.Set(temp);
			return temp;
		}
	}
	
	/// <summary>
	/// SharpDX MiniCube Direct3D 11 Sample
	/// </summary>
	internal class Program
	{
		
		//	  [STAThread]
		private static void Main()
		{
			new Program();
		}
		
		UN11 uneleven;

		RenderForm form;
		
		Device device;
		SwapChainDescription desc;
		SwapChain swapChain;
		DeviceContext context;
		
		Factory factory;
		
		UN11.ViewTrans vt;
		
		UN11.View view;
		UN11.Over over;
		UN11.Face face;
		UN11.Light sun;
		UN11.TexElem telem;
		UN11.ViewDrawData vddat;
		UN11.OverDrawData oddat;
		UN11.FaceDrawData cddat;
		UN11.FrameDrawData fddat;
		
		Stopwatch clock;
		
		Texture2D backBuffer = null;
		RenderTargetView renderView = null;
		Texture2D depthBuffer = null;
		DepthStencilView depthView = null;
		
		bool userResized = true;
		
		public Program()
		{
			form = new RenderForm("SharpDX - MiniCube Direct3D11 Sample");

			// SwapChain description
			desc = new SwapChainDescription()
			{
				BufferCount = 1,
				ModeDescription =
					new ModeDescription(form.ClientSize.Width, form.ClientSize.Height,
					                    new Rational(60, 1), Format.R8G8B8A8_UNorm),
				IsWindowed = true,
				OutputHandle = form.Handle,
				SampleDescription = new SampleDescription(1, 0),
				SwapEffect = SwapEffect.Discard,
				Usage = Usage.RenderTargetOutput
			};

			// Used for debugging dispose object references
			// Configuration.EnableObjectTracking = true;

			// Disable throws on shader compilation errors
			//Configuration.ThrowOnShaderCompileError = false;

			// Create Device and SwapChain
			Device.CreateWithSwapChain(DriverType.Hardware, DeviceCreationFlags.None, desc, out device, out swapChain);
			context = device.ImmediateContext;
			
			uneleven = new UN11(device); // UN11 knows about your device!

			// Ignore all windows events
			factory = swapChain.GetParent<Factory>();
			factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll);

			// Compile Vertex and Pixel shaders
			// let's try it from a file! :D
			uneleven.loadTechniquesFromFile("textT.uncrz");
			/*
			UN11.ShaderBytecodeDesc VSdesc = uneleven.loadShaderBytecode("MiniCube.fx", "VShade", "vs_4_0");
			UN11.ShaderBytecodeDesc PSdesc = uneleven.loadShaderBytecode("MiniCube.fx", "PShade", "ps_4_0");
			UN11.ShaderBytecodeDesc VSdesc2 = uneleven.loadShaderBytecode("MiniCube.fx", "VShade2", "vs_4_0");
			UN11.ShaderBytecodeDesc PSdesc2 = uneleven.loadShaderBytecode("MiniCube.fx", "PShade2", "ps_4_0");

			uneleven.createTechnique("dull", UN11.VertexType.VertexPC, VSdesc, PSdesc);
			uneleven.createTechnique("dull2", UN11.VertexType.VertexPC, VSdesc2, PSdesc2);
			 */
			
			// ....load some more stuff from files??
			uneleven.loadModelsFromFile("text.uncrz", context);
			
			// describe frame
			view = new UN11.View(device, "main", uneleven.matrices);
			over = new UN11.Over(device, "main");
			face = new UN11.Face(device, "main");
			sun = new UN11.Light(device, "sun", uneleven.matrices);
			telem = new UN11.TexElem("disp", null, new Rectangle(0, 0, view.texHeight, view.texHeight));
			
			fddat = new UN11.FrameDrawData();
			vddat = new UN11.ViewDrawData(view);
			oddat = new UN11.OverDrawData(over);
			cddat = new UN11.FaceDrawData(face, vt);
			
			vddat.geometryDrawDatas.Add(new UN11.CubeDrawData(new UN11.Cube(device)));
			vddat.lights.Add(sun);
			
			UN11.ModelEntity tent = new UN11.ModelEntity(new UN11.Model(uneleven.models["tree0"], device, context, true), "tent");
			tent.update(true);
			vddat.geometryDrawDatas.Add(new UN11.ModelEntityDrawData(tent));
			
			cddat.elems.Add(telem);
			
			// lots of trees?!
			Random rnd = new Random();
			int n = 100;
			UN11.ManyModelDrawData mmddat = new UN11.ManyModelDrawData(uneleven.models["tree0"]);
			mmddat.useOwnSections = false;
			for (int i = 0; i < n * n * n / 1000; i++)
			{
				tent = new UN11.ModelEntity(new UN11.Model(uneleven.models["tree0"], device, context, false), "tent" + i);
				tent.or.offset = new Vector3(rnd.NextFloat(-n, n), -20, rnd.NextFloat(-n, n));
				tent.update(true);
				mmddat.models.Add(tent.mdl);
			}
			vddat.geometryDrawDatas.Add(mmddat);
			//
			
			uneleven.slides.Add(view);
			uneleven.slides.Add(over);
			
			fddat.slideDrawDatas.Add(vddat);
			fddat.slideDrawDatas.Add(oddat);
			fddat.slideDrawDatas.Add(cddat);

			// Use clock
			clock = new Stopwatch();
			clock.Start();

			// Declare texture for rendering
			backBuffer = null;
			renderView = null;
			depthBuffer = null;
			depthView = null;

			// Setup handler on resize form
			form.UserResized += (sender, args) => userResized = true;

			// Setup full screen mode change F5 (Full) F4 (Window)
			form.KeyUp += (sender, args) =>
			{
				if (args.KeyCode == Keys.F5)
					swapChain.SetFullscreenState(true, null);
				else if (args.KeyCode == Keys.F4)
					swapChain.SetFullscreenState(false, null);
				else if (args.KeyCode == Keys.Escape)
					form.Close();
			};
			
			perform();
		}
		
		void perform()
		{
			// Main loop
			RenderLoop.Run(form, frame);

			// Release all resources
			//vertexBuffer.Dispose();
			depthBuffer.Dispose();
			depthView.Dispose();
			renderView.Dispose();
			backBuffer.Dispose();
			context.ClearState();
			context.Flush();
			device.Dispose();
			context.Dispose();
			swapChain.Dispose();
			factory.Dispose();
			
			uneleven.disposeAll();
		}
		
		void frame()
		{
			//uneleven.techniques.Get("dull2").passes[0].apply(context);
			
			// If Form resized
			if (userResized)
			{
				if (form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0)
					return;
				
				// Dispose all previous allocated resources
				Utilities.Dispose(ref backBuffer);
				Utilities.Dispose(ref renderView);
				Utilities.Dispose(ref depthBuffer);
				Utilities.Dispose(ref depthView);

				// FIXME: crashes when we try and re-size me, probably a good reason
				// note that any of the resources disposed above or rebuilt below may be used by slides or geometry
				
				// Resize the backbuffer
				swapChain.ResizeBuffers(desc.BufferCount, form.ClientSize.Width, form.ClientSize.Height, Format.Unknown, SwapChainFlags.None);

				// Get the backbuffer from the swapchain
				backBuffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);

				// Renderview on the backbuffer
				renderView = new RenderTargetView(device, backBuffer);

				// Create the depth buffer
				depthBuffer = new Texture2D(device, new Texture2DDescription()
				                            {
				                            	Format = Format.D32_Float_S8X24_UInt,
				                            	ArraySize = 1,
				                            	MipLevels = 1,
				                            	Width = form.ClientSize.Width,
				                            	Height = form.ClientSize.Height,
				                            	SampleDescription = new SampleDescription(1, 0),
				                            	Usage = ResourceUsage.Default,
				                            	BindFlags = BindFlags.DepthStencil,
				                            	CpuAccessFlags = CpuAccessFlags.None,
				                            	OptionFlags = ResourceOptionFlags.None
				                            });

				// Create the depth buffer view
				depthView = new DepthStencilView(device, depthBuffer);

				// Setup targets and viewport for rendering
				//context.Rasterizer.SetViewport(new Viewport(0, 0, form.ClientSize.Width, form.ClientSize.Height, 0.0f, 1.0f));
				//context.OutputMerger.SetTargets(depthView, renderView);
				
				vt = new UN11.ViewTrans(form.ClientSize.Width, form.ClientSize.Height, form.ClientSize.Width, form.ClientSize.Height);
					
				// set up view with correct aspect ratio
				view.setDimension(form.ClientSize.Width, form.ClientSize.Height);
				view.setProj(UN11.ViewMode.Persp, (float)Math.PI / 4.0f, form.ClientSize.Width / (float)form.ClientSize.Height, 0.1f, 1000.0f);
				//view.initTarget(renderView);
				view.initTarget(device, Format.R32G32B32A32_Float, uneleven.textures);
				//view.initTarget(context.OutputMerger.GetRenderTargets(1)[0]);
				view.initSide(device, Format.R32G32B32A32_Float, uneleven.textures);
				//view.initTargetStencil(depthView);
				view.initTargetStencil(device);
				view.initSideStencil(device);
				view.initOverness(device);
				view.clearColour = Color.DarkOliveGreen;
				
				// set up over
				over.setDimension(view.texWidth, view.texHeight);
				over.initOverness(device);
				over.initTarget(device, Format.R32G32B32A32_Float, uneleven.textures);
				over.initTargetStencil(device);
				over.texness.tex = uneleven.textures["view_main"];
				over.texness.useTex = true;
				over.tech = uneleven.techniques["simpleOver"];
				over.clearColour = Color.DeepPink;
				
				// set up face
				face.setDimension(view.texWidth, view.texHeight);
				face.initTarget(renderView);
				face.clearColour = Color.BlanchedAlmond;
				
				// set up top element
				telem.texness.tex = uneleven.textures["over_main"];
				telem.texness.useTex = true;
				telem.tech = uneleven.techniques["simpleFace"];
				telem.rect = new Rectangle(0, 0, view.texWidth, view.texHeight);
				telem.colmod = new Vector4(1f, 1f, 1f, 1f);
				
				// set up sun
				sun.useLightMap = false;
				sun.lightType = UN11.LightType.Point;
				sun.dimX = 50;
				sun.dimY = 50;
				sun.lightDepth = 50;
				sun.lightAmbient = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
				sun.lightColMod = new Vector4(1, 1, 1, 1);
				sun.lightPos = new Vector3(0, 10, 5);
				sun.lightEnabled = true;
				
				// We are done resizing
				userResized = false;
			}
			
			var time = clock.ElapsedMilliseconds / 10000.0f;

			// Clear views
			//context.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1.0f, 0);
			//context.ClearRendedothrTargetView(renderView, Color.Black);

			// Update WorldViewProj Matrix
			var rot = Matrix.RotationX(time) * Matrix.RotationY(time * 2) * Matrix.RotationZ(time * .7f);
			Vector3 span = new Vector3(15, 0, 0);
			Vector3.TransformCoordinate(ref span, ref rot, out span);
			
//			var viewMat = Matrix.LookAtLH(new Vector3(0, 0, -5), new Vector3(0, 0, 0), Vector3.UnitY);
//			var projMat = Matrix.PerspectiveFovLH((float)Math.PI / 4.0f, form.ClientSize.Width / (float)form.ClientSize.Height, 0.1f, 100.0f);
//			var viewProj = Matrix.Multiply(viewMat, projMat);
//			var worldViewProj = Matrix.RotationX(time) * Matrix.RotationY(time * 2) * Matrix.RotationZ(time * .7f) * viewProj;
//			worldViewProj.Transpose();
//			eyeBuffer = new UN11.ConstBuffer<UN11.EyeCData>(device, UN11.EyeCData.defaultSlot);
//			eyeBuffer.data.viewProj = worldViewProj;
			
			view.camPos = span;
			view.dirNormalAt(Vector3.Zero);
			
			// REAL STUFF
			
			// TODO: work out an UN11.updateAll() method or something, perhaps (I don't think this makes sense)
			view.update();
			sun.update();
			telem.update(vt, true);


			uneleven.tick();
			uneleven.drawFrame(context, fddat);
			

			// Present!
			swapChain.Present(0, PresentFlags.None);
			//SharpDX.Direct3D11.Resource.ToFile(context, over.targetRenderViewPair.renderView.Resource, ImageFileFormat.Bmp, "main.bmp");

			// END REAL STUFF
			
			form.Text = time.ToString();
		}
	}
}