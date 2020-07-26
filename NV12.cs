using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace FNA
{
    public class NV12 : IDisposable
    {
        #region Hardware-accelerated YUV -> RGBA
        GraphicsDevice currentDevice;
        private Effect shaderProgram;
        private IntPtr stateChangesPtr;
        private Texture2D[] yuvTextures = new Texture2D[2];
        private Viewport viewport;

        private static VertexPositionTexture[] vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(
                new Vector3(-1.0f, -1.0f, 0.0f),
                new Vector2(0.0f, 1.0f)
            ),
            new VertexPositionTexture(
                new Vector3(1.0f, -1.0f, 0.0f),
                new Vector2(1.0f, 1.0f)
            ),
            new VertexPositionTexture(
                new Vector3(-1.0f, 1.0f, 0.0f),
                new Vector2(0.0f, 0.0f)
            ),
            new VertexPositionTexture(
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector2(1.0f, 0.0f)
            )
        };
        private VertexBufferBinding vertBuffer;

        // Used to restore our previous GL state.
        private Texture[] oldTextures = new Texture[2];
        private SamplerState[] oldSamplers = new SamplerState[2];
        private RenderTargetBinding[] oldTargets;
        private VertexBufferBinding[] oldBuffers;

        // Store this to optimize things on our end.
        private RenderTargetBinding[] videoTexture;

        private BlendState prevBlend;
        private DepthStencilState prevDepthStencil;
        private RasterizerState prevRasterizer;
        private Viewport prevViewport;
        int _width;
        int _height;
        private void GL_initialize()
        {

            unsafe
            {
                stateChangesPtr = Marshal.AllocHGlobal(
                    sizeof(MojoShader.MOJOSHADER_effectStateChanges)
                );
            }

            // Allocate the vertex buffer
            vertBuffer = new VertexBufferBinding(
                new VertexBuffer(
                    currentDevice,
                    VertexPositionTexture.VertexDeclaration,
                    4,
                    BufferUsage.WriteOnly
                )
            );
            vertBuffer.VertexBuffer.SetData(vertices);
        }

        private void GL_dispose()
        {
            if (currentDevice == null)
            {
                // We never initialized to begin with...
                return;
            }
            currentDevice = null;

            // Delete the Effect
            if (shaderProgram != null)
            {
                shaderProgram.Dispose();
            }
            if (stateChangesPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stateChangesPtr);
            }

            // Delete the vertex buffer
            if (vertBuffer.VertexBuffer != null)
            {
                vertBuffer.VertexBuffer.Dispose();
            }

            // Delete the textures if they exist
            for (int i = 0; i < 2; i += 1)
            {
                if (yuvTextures[i] != null)
                {
                    yuvTextures[i].Dispose();
                }
            }
        }

        private void GL_setupTextures(int width, int height)
        {
            // Allocate YUV GL textures
            for (int i = 0; i < 2; i += 1)
            {
                if (yuvTextures[i] != null)
                {
                    yuvTextures[i].Dispose();
                }
            }
            yuvTextures[0] = new Texture2D(
                currentDevice,
                width,
                height,
                false,
                SurfaceFormat.Alpha8
            );
            yuvTextures[1] = new Texture2D(
                currentDevice,
                width / 2,
                height / 2,
                false,
                SurfaceFormat.NormalizedByte2
            );


            // Precalculate the viewport
            viewport = new Viewport(0, 0, width, height);
        }


        private void GL_pushState()
        {
            // Begin the effect, flagging to restore previous state on end
            currentDevice.GLDevice.BeginPassRestore(
                shaderProgram.glEffect,
                stateChangesPtr
            );

            // Prep our samplers
            for (int i = 0; i < 2; i += 1)
            {
                oldTextures[i] = currentDevice.Textures[i];
                oldSamplers[i] = currentDevice.SamplerStates[i];
                currentDevice.Textures[i] = yuvTextures[i];
                currentDevice.SamplerStates[i] = SamplerState.LinearClamp;
            }

            // Prep buffers
            oldBuffers = currentDevice.GetVertexBuffers();
            currentDevice.SetVertexBuffers(vertBuffer);

            // Prep target bindings
            oldTargets = currentDevice.GetRenderTargets();
            currentDevice.GLDevice.SetRenderTargets(
                this.videoTexture,
                null,
                DepthFormat.None
            );

            // Prep render state
            prevBlend = currentDevice.BlendState;
            prevDepthStencil = currentDevice.DepthStencilState;
            prevRasterizer = currentDevice.RasterizerState;
            currentDevice.BlendState = BlendState.Opaque;
            currentDevice.DepthStencilState = DepthStencilState.None;
            currentDevice.RasterizerState = RasterizerState.CullNone;

            // Prep viewport
            prevViewport = currentDevice.Viewport;
            currentDevice.GLDevice.SetViewport(viewport);
        }


        private void GL_popState()
        {
            // End the effect, restoring the previous shader state
            currentDevice.GLDevice.EndPassRestore(shaderProgram.glEffect);

            // Restore GL state
            currentDevice.BlendState = prevBlend;
            currentDevice.DepthStencilState = prevDepthStencil;
            currentDevice.RasterizerState = prevRasterizer;
            prevBlend = null;
            prevDepthStencil = null;
            prevRasterizer = null;

            /* Restore targets using GLDevice directly.
			 * This prevents accidental clearing of previously bound targets.
			 */
            if (oldTargets == null || oldTargets.Length == 0)
            {
                currentDevice.GLDevice.SetRenderTargets(
                    null,
                    null,
                    DepthFormat.None
                );
            }
            else
            {
                IRenderTarget oldTarget = oldTargets[0].RenderTarget as IRenderTarget;
                currentDevice.GLDevice.SetRenderTargets(
                    oldTargets,
                    oldTarget.DepthStencilBuffer,
                    oldTarget.DepthStencilFormat
                );
            }
            oldTargets = null;

            // Set viewport AFTER setting targets!
            currentDevice.GLDevice.SetViewport(prevViewport);

            // Restore buffers
            currentDevice.SetVertexBuffers(oldBuffers);
            oldBuffers = null;

            // Restore samplers
            for (int i = 0; i < 2; i += 1)
            {
                /* The application may have set a texture ages
				 * ago, only to not unset after disposing. We
				 * have to avoid an ObjectDisposedException!
				 */
                if (oldTextures[i] == null || !oldTextures[i].IsDisposed)
                {
                    currentDevice.Textures[i] = oldTextures[i];
                }
                currentDevice.SamplerStates[i] = oldSamplers[i];
                oldTextures[i] = null;
                oldSamplers[i] = null;
            }
        }

        public void Dispose()
        {
            GL_dispose();
        }

        public unsafe void Update(void* YUV,int width, int height){
            if(viewport.Width != width || viewport.Height != height)
            {
                GL_setupTextures(width,height);
            }
            // Prepare YUV GL textures with our current frame data
            var device = currentDevice.GLDevice as OpenGLDevice;
            device.SetTextureDataNV12(yuvTextures[0], yuvTextures[1], new IntPtr(YUV));
            // Draw the YUV textures to the framebuffer with our shader.
            GL_pushState();
            currentDevice.DrawPrimitives(
				PrimitiveType.TriangleStrip,
				0,
				2
			);
            GL_popState();

    }
        #endregion
        //
        //
        // Load the YUV->RGBA Effect
         public NV12(RenderTarget2D outputTexture)
        {
            this.currentDevice = outputTexture.GraphicsDevice;
            this.shaderProgram = new Effect(currentDevice,Resources.NV12ToRGBAEffect);

            videoTexture = new RenderTargetBinding[1];
            videoTexture[0] = outputTexture;
            GL_initialize();
        }


    }
}
